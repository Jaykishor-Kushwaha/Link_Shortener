using MongoDB.Bson;
using MongoDB.Driver;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Services;

/// <summary>
/// Analytics service using MongoDB aggregation pipelines.
/// Both queries are index-backed and bounded — no documents are loaded into application memory.
/// At 10M click events, these remain performant because:
/// - Time-series: $match on (urlId, timestamp range) uses compound index, $group runs on the bounded set
/// - Top referrers: $match on urlId uses compound index (urlId, referrer), $group runs server-side
/// </summary>
public class AnalyticsService
{
    private readonly MongoDbContext _db;

    public AnalyticsService(MongoDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns a time series of click counts grouped by hour or day.
    /// Uses $dateTrunc for bucket alignment and the (urlId, timestamp) compound index.
    /// </summary>
    public async Task<List<TimeSeriesBucket>> GetTimeSeriesAsync(
        string urlId, DateTime from, DateTime to, string bucket = "hour")
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument
            {
                { "urlId", new ObjectId(urlId) },
                { "timestamp", new BsonDocument
                    {
                        { "$gte", from.ToUniversalTime() },
                        { "$lte", to.ToUniversalTime() }
                    }
                }
            }),
            new("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$dateTrunc", new BsonDocument
                    {
                        { "date", "$timestamp" },
                        { "unit", bucket }
                    })
                },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new("$sort", new BsonDocument("_id", 1)),
            new("$project", new BsonDocument
            {
                { "_id", 0 },
                { "bucket", "$_id" },
                { "count", 1 }
            })
        };

        var results = await _db.ClickEvents
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync();

        return results.Select(doc => new TimeSeriesBucket
        {
            Bucket = doc["bucket"].ToUniversalTime(),
            Count = doc["count"].ToInt64()
        }).ToList();
    }

    /// <summary>
    /// Returns top referrers by click count for a given URL.
    /// Uses the (urlId, referrer) compound index for efficient aggregation.
    /// </summary>
    public async Task<List<ReferrerStat>> GetTopReferrersAsync(string urlId, int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 100);

        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("urlId", new ObjectId(urlId))),
            new("$group", new BsonDocument
            {
                { "_id", "$referrer" },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new("$sort", new BsonDocument("count", -1)),
            new("$limit", limit),
            new("$project", new BsonDocument
            {
                { "_id", 0 },
                { "referrer", "$_id" },
                { "count", 1 }
            })
        };

        var results = await _db.ClickEvents
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync();

        return results.Select(doc => new ReferrerStat
        {
            Referrer = doc["referrer"].AsString,
            Count = doc["count"].ToInt64()
        }).ToList();
    }
}
