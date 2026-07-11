using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace UrlShortener.Api.Models;

/// <summary>
/// Immutable click event — every redirect writes one of these.
/// Indexed on (urlId, timestamp) for time-series queries and (urlId, referrer) for top-referrer aggregation.
/// </summary>
public class ClickEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("urlId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UrlId { get; set; } = null!;

    [BsonElement("shortCode")]
    public string ShortCode { get; set; } = null!;

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("referrer")]
    public string Referrer { get; set; } = string.Empty;

    [BsonElement("userAgent")]
    public string UserAgent { get; set; } = string.Empty;

    [BsonElement("ip")]
    public string Ip { get; set; } = string.Empty;
}
