using StackExchange.Redis;
using UrlShortener.Api.Configuration;

namespace UrlShortener.Api.Infrastructure;

/// <summary>
/// Redis wrapper for URL caching and rate limiting.
/// All state is in Redis, so rate limits survive process restarts and are correct across multiple app instances.
/// </summary>
public class RedisService
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly int _cacheTtlSeconds;

    private const string UrlCachePrefix = "url:";
    private const string RateLimitPrefix = "rl:";

    public RedisService(IConnectionMultiplexer multiplexer, AppSettings settings)
    {
        _multiplexer = multiplexer;
        _db = multiplexer.GetDatabase();
        _cacheTtlSeconds = settings.CacheTtlSeconds;
    }

    // ── URL Caching ──

    /// <summary>
    /// Returns the cached destination URL for a short code, or null on cache miss.
    /// </summary>
    public async Task<string?> GetCachedUrlAsync(string shortCode)
    {
        var value = await _db.StringGetAsync($"{UrlCachePrefix}{shortCode}");
        return value.HasValue ? value.ToString() : null;
    }

    /// <summary>
    /// Caches a short code → destination mapping with the configured TTL.
    /// TTL of 5 minutes balances freshness vs. MongoDB load on the redirect hot path.
    /// </summary>
    public async Task SetCachedUrlAsync(string shortCode, string longUrl)
    {
        await _db.StringSetAsync(
            $"{UrlCachePrefix}{shortCode}",
            longUrl,
            TimeSpan.FromSeconds(_cacheTtlSeconds));
    }

    /// <summary>
    /// Stores a sentinel "GONE" value so that subsequent redirects don't hit MongoDB
    /// for deleted/expired links. Same TTL as normal cache entries.
    /// </summary>
    public async Task SetGoneAsync(string shortCode)
    {
        await _db.StringSetAsync(
            $"{UrlCachePrefix}{shortCode}",
            "__GONE__",
            TimeSpan.FromSeconds(_cacheTtlSeconds));
    }

    /// <summary>
    /// Invalidates the cache for a short code. Called on PATCH and DELETE
    /// to prevent stale destinations from being served.
    /// </summary>
    public async Task InvalidateUrlAsync(string shortCode)
    {
        await _db.KeyDeleteAsync($"{UrlCachePrefix}{shortCode}");
    }

    public bool IsGoneSentinel(string? value) => value == "__GONE__";

    // ── Rate Limiting ──
    // Uses Redis INCR + EXPIRE for a fixed-window rate limiter.
    // The key encodes the window boundary so it auto-expires.

    /// <summary>
    /// Checks if a request should be allowed under the rate limit.
    /// Returns (allowed, retryAfterSeconds).
    /// </summary>
    public async Task<(bool Allowed, int RetryAfterSeconds)> CheckRateLimitAsync(
        string key, int maxRequests, int windowSeconds)
    {
        var windowKey = $"{RateLimitPrefix}{key}:{GetWindowId(windowSeconds)}";

        var count = await _db.StringIncrementAsync(windowKey);

        // Set expiry on the first request in this window
        if (count == 1)
        {
            await _db.KeyExpireAsync(windowKey, TimeSpan.FromSeconds(windowSeconds));
        }

        if (count > maxRequests)
        {
            var ttl = await _db.KeyTimeToLiveAsync(windowKey);
            var retryAfter = ttl.HasValue ? (int)Math.Ceiling(ttl.Value.TotalSeconds) : windowSeconds;
            return (false, retryAfter);
        }

        return (true, 0);
    }

    private static long GetWindowId(int windowSeconds)
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSeconds;
    }

    // ── Health Check ──

    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            var pong = await _db.PingAsync();
            return pong.TotalMilliseconds < 5000;
        }
        catch
        {
            return false;
        }
    }
}
