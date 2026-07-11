using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using UrlShortener.Api.Configuration;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Models;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Validators;

namespace UrlShortener.Api.Services;

public class UrlService
{
    private readonly MongoDbContext _db;
    private readonly RedisService _redis;
    private readonly AppSettings _settings;
    private readonly ChannelWriter<ClickEvent> _clickChannel;
    private readonly ILogger<UrlService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const int MaxRetries = 5;

    public UrlService(
        MongoDbContext db,
        RedisService redis,
        AppSettings settings,
        Channel<ClickEvent> clickChannel,
        ILogger<UrlService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _redis = redis;
        _settings = settings;
        _clickChannel = clickChannel.Writer;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Creates a shortened URL. Uses the unique index on shortCode as the concurrency guard:
    /// on duplicate key error, retries with a new random code.
    /// Custom aliases go through the same insert path — they compete for the same namespace.
    /// </summary>
    public async Task<UrlResponse> CreateAsync(string userId, CreateUrlRequest request)
    {
        await UrlValidator.ValidateDestinationUrlAsync(request.LongUrl);

        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value.ToUniversalTime() <= DateTime.UtcNow)
            throw new AppException("Expiry must be in the future.", 400);

        var shortCode = request.CustomAlias?.Trim() ?? ShortCodeGenerator.Generate();

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var url = new ShortenedUrl
            {
                ShortCode = shortCode,
                LongUrl = request.LongUrl,
                UserId = userId,
                ClickCount = 0,
                ExpiresAt = request.ExpiresAt?.ToUniversalTime(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                await _db.Urls.InsertOneAsync(url);
                _logger.LogInformation("URL created: {ShortCode} -> {LongUrl} by {UserId}", shortCode, request.LongUrl, userId);

                return ToResponse(url);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // If this was a custom alias, don't retry — tell the user it's taken
                if (request.CustomAlias != null)
                    throw new AppException($"Alias '{request.CustomAlias}' is already taken.", 409);

                // Generated code collision — retry with a new random code
                shortCode = ShortCodeGenerator.Generate();
                _logger.LogDebug("Short code collision on attempt {Attempt}, retrying.", attempt + 1);
            }
        }

        // Exhausted retries — extremely unlikely with 48 bits of entropy
        throw new AppException("Unable to generate a unique short code. Please try again.", 500);
    }

    /// <summary>
    /// Resolves a short code to its destination URL.
    /// Hot path: Redis first, MongoDB fallback, then cache the result.
    /// Click recording is fire-and-forget via Channel — no MongoDB write on the request path.
    /// Click count increment uses atomic $inc — no read-modify-write race.
    /// </summary>
    public async Task<(string? LongUrl, bool IsGone, string? UrlId)> ResolveAsync(
        string shortCode, string ip, string referrer, string userAgent)
    {
        // 1. Check Redis cache
        var cached = await _redis.GetCachedUrlAsync(shortCode);
        if (cached != null)
        {
            if (_redis.IsGoneSentinel(cached))
                return (null, true, null);

            // Fire-and-forget: record the click asynchronously
            // We need the urlId for analytics, so look it up from the code
            _ = RecordClickFromCacheAsync(shortCode, ip, referrer, userAgent);
            return (cached, false, null);
        }

        // 2. Cache miss — query MongoDB
        var url = await _db.Urls.Find(u => u.ShortCode == shortCode).FirstOrDefaultAsync();

        if (url == null)
            return (null, false, null); // 404

        if (url.IsGone)
        {
            await _redis.SetGoneAsync(shortCode);
            return (null, true, url.Id);
        }

        // 3. Cache the result
        await _redis.SetCachedUrlAsync(shortCode, url.LongUrl);

        // 4. Record click (fire-and-forget to Channel, atomic $inc to MongoDB)
        await RecordClickAsync(url.Id, shortCode, ip, referrer, userAgent);

        return (url.LongUrl, false, url.Id);
    }

    /// <summary>
    /// Updates the destination of a link. Ownership is enforced in the query filter:
    /// the update only matches if both _id and userId match. Returns null if not found/not owned.
    /// Invalidates Redis cache immediately so the next redirect serves the new destination.
    /// </summary>
    public async Task<UrlResponse?> UpdateAsync(string userId, string urlId, UpdateUrlRequest request)
    {
        await UrlValidator.ValidateDestinationUrlAsync(request.LongUrl);

        var filter = Builders<ShortenedUrl>.Filter.And(
            Builders<ShortenedUrl>.Filter.Eq(u => u.Id, urlId),
            Builders<ShortenedUrl>.Filter.Eq(u => u.UserId, userId),
            Builders<ShortenedUrl>.Filter.Eq(u => u.DeletedAt, null));

        var update = Builders<ShortenedUrl>.Update
            .Set(u => u.LongUrl, request.LongUrl)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        var options = new FindOneAndUpdateOptions<ShortenedUrl>
        {
            ReturnDocument = ReturnDocument.After
        };

        var updated = await _db.Urls.FindOneAndUpdateAsync(filter, update, options);
        if (updated == null) return null;

        // Invalidate cache so the next redirect picks up the new destination
        await _redis.InvalidateUrlAsync(updated.ShortCode);

        _logger.LogInformation("URL updated: {ShortCode} -> {NewUrl} by {UserId}", updated.ShortCode, request.LongUrl, userId);
        return ToResponse(updated);
    }

    /// <summary>
    /// Soft-deletes a link. Sets deletedAt timestamp, doesn't remove the document.
    /// Ownership enforced in the query filter. Invalidates Redis cache.
    /// </summary>
    public async Task<bool> DeleteAsync(string userId, string urlId)
    {
        var filter = Builders<ShortenedUrl>.Filter.And(
            Builders<ShortenedUrl>.Filter.Eq(u => u.Id, urlId),
            Builders<ShortenedUrl>.Filter.Eq(u => u.UserId, userId),
            Builders<ShortenedUrl>.Filter.Eq(u => u.DeletedAt, null));

        var update = Builders<ShortenedUrl>.Update
            .Set(u => u.DeletedAt, DateTime.UtcNow)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        var result = await _db.Urls.FindOneAndUpdateAsync(filter, update);
        if (result == null) return false;

        // Invalidate cache so subsequent redirects return 410
        await _redis.InvalidateUrlAsync(result.ShortCode);

        _logger.LogInformation("URL deleted: {ShortCode} by {UserId}", result.ShortCode, userId);
        return true;
    }

    /// <summary>
    /// Lists the caller's links with pagination. Excludes soft-deleted links.
    /// </summary>
    public async Task<PaginatedResponse<UrlResponse>> ListAsync(string userId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var filter = Builders<ShortenedUrl>.Filter.And(
            Builders<ShortenedUrl>.Filter.Eq(u => u.UserId, userId),
            Builders<ShortenedUrl>.Filter.Eq(u => u.DeletedAt, null));

        var total = await _db.Urls.CountDocumentsAsync(filter);

        var items = await _db.Urls
            .Find(filter)
            .SortByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return new PaginatedResponse<UrlResponse>
        {
            Items = items.Select(ToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Gets a single URL by ID. Used by analytics endpoints to verify ownership.
    /// </summary>
    public async Task<ShortenedUrl?> GetByIdAsync(string urlId)
    {
        return await _db.Urls.Find(u => u.Id == urlId).FirstOrDefaultAsync();
    }

    // ── Click Recording ──

    /// <summary>
    /// Records a click: atomic $inc on the click count (no race condition),
    /// and writes the full event to a Channel for background batch insertion.
    /// The redirect response does not wait for the event insertion to MongoDB.
    /// </summary>
    private async Task RecordClickAsync(string urlId, string shortCode, string ip, string referrer, string userAgent)
    {
        // Atomic increment — no read-modify-write race condition
        var incUpdate = Builders<ShortenedUrl>.Update.Inc(u => u.ClickCount, 1);
        _ = _db.Urls.UpdateOneAsync(u => u.Id == urlId, incUpdate);

        var clickEvent = new ClickEvent
        {
            UrlId = urlId,
            ShortCode = shortCode,
            Timestamp = DateTime.UtcNow,
            Referrer = referrer,
            UserAgent = userAgent,
            Ip = ip
        };

        // Write to channel — does not block on MongoDB. Background service drains this.
        await _clickChannel.WriteAsync(clickEvent);
    }

    /// <summary>
    /// When we have a cache hit, we don't have the urlId readily available.
    /// Look it up, then record the click.
    /// </summary>
    private async Task RecordClickFromCacheAsync(string shortCode, string ip, string referrer, string userAgent)
    {
        try
        {
            var url = await _db.Urls.Find(u => u.ShortCode == shortCode).FirstOrDefaultAsync();
            if (url != null)
            {
                await RecordClickAsync(url.Id, shortCode, ip, referrer, userAgent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording click for cached URL {ShortCode}", shortCode);
        }
    }

    private UrlResponse ToResponse(ShortenedUrl url)
    {
        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl) || baseUrl.Contains("localhost"))
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                var request = httpContext.Request;
                var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
                var host = request.Host.Value;
                baseUrl = $"{scheme}://{host}";
            }
        }

        return new UrlResponse
        {
            Id = url.Id,
            ShortCode = url.ShortCode,
            ShortUrl = $"{baseUrl}/{url.ShortCode}",
            LongUrl = url.LongUrl,
            ClickCount = url.ClickCount,
            ExpiresAt = url.ExpiresAt,
            CreatedAt = url.CreatedAt
        };
    }
}
