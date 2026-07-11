using MongoDB.Driver;
using UrlShortener.Api.Configuration;
using UrlShortener.Api.Models;

namespace UrlShortener.Api.Infrastructure;

/// <summary>
/// Singleton MongoDB context. Creates collections and indexes on initialization.
/// All index creation is idempotent — safe to call on every startup.
/// </summary>
public class MongoDbContext
{
    public IMongoDatabase Database { get; }
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<ShortenedUrl> Urls { get; }
    public IMongoCollection<ClickEvent> ClickEvents { get; }
    public IMongoCollection<RefreshToken> RefreshTokens { get; }

    public MongoDbContext(AppSettings settings)
    {
        var client = new MongoClient(settings.MongoUri);
        Database = client.GetDatabase(new MongoUrl(settings.MongoUri).DatabaseName ?? "url_shortener");

        Users = Database.GetCollection<User>("users");
        Urls = Database.GetCollection<ShortenedUrl>("shortenedUrls");
        ClickEvents = Database.GetCollection<ClickEvent>("clickEvents");
        RefreshTokens = Database.GetCollection<RefreshToken>("refreshTokens");
    }

    /// <summary>
    /// Creates all required indexes. The unique index on shortCode is the collision-prevention mechanism:
    /// concurrent inserts with the same code will get a duplicate key error (E11000), and the service retries
    /// with a new random code. This is a database-level guarantee, not an application-level lock.
    /// </summary>
    public async Task CreateIndexesAsync()
    {
        // Users: unique email
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true }));

        // ShortenedUrls: unique shortCode — the core concurrency guarantee
        await Urls.Indexes.CreateOneAsync(
            new CreateIndexModel<ShortenedUrl>(
                Builders<ShortenedUrl>.IndexKeys.Ascending(u => u.ShortCode),
                new CreateIndexOptions { Unique = true }));

        // ShortenedUrls: userId + deletedAt for listing user's active links
        await Urls.Indexes.CreateOneAsync(
            new CreateIndexModel<ShortenedUrl>(
                Builders<ShortenedUrl>.IndexKeys
                    .Ascending(u => u.UserId)
                    .Ascending(u => u.DeletedAt)
                    .Descending(u => u.CreatedAt)));

        // ClickEvents: (urlId, timestamp) for time-series analytics queries
        await ClickEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<ClickEvent>(
                Builders<ClickEvent>.IndexKeys
                    .Ascending(c => c.UrlId)
                    .Ascending(c => c.Timestamp)));

        // ClickEvents: (urlId, referrer) for top-referrer aggregation
        await ClickEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<ClickEvent>(
                Builders<ClickEvent>.IndexKeys
                    .Ascending(c => c.UrlId)
                    .Ascending(c => c.Referrer)));

        // RefreshTokens: tokenHash for lookup during refresh
        await RefreshTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(r => r.TokenHash)));

        // RefreshTokens: family for invalidating entire rotation chain
        await RefreshTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(r => r.Family)));

        // RefreshTokens: TTL index auto-removes expired tokens
        await RefreshTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(r => r.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));
    }
}
