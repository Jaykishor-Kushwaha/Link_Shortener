using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly MongoDbContext _db;
    private readonly RedisService _redis;

    public HealthController(MongoDbContext db, RedisService redis)
    {
        _db = db;
        _redis = redis;
    }

    /// <summary>
    /// GET /health — reports MongoDB and Redis reachability.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> HealthCheck()
    {
        var mongoOk = false;
        var redisOk = false;

        try
        {
            // Ping MongoDB
            await _db.Database.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1));
            mongoOk = true;
        }
        catch { }

        try
        {
            redisOk = await _redis.IsConnectedAsync();
        }
        catch { }

        var status = mongoOk && redisOk ? "healthy" : "degraded";
        var statusCode = mongoOk && redisOk ? 200 : 503;

        return StatusCode(statusCode, new
        {
            status,
            mongo = mongoOk ? "connected" : "disconnected",
            redis = redisOk ? "connected" : "disconnected",
            timestamp = DateTime.UtcNow
        });
    }
}
