using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace UrlShortener.Api.Models;

public class ShortenedUrl
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("shortCode")]
    public string ShortCode { get; set; } = null!;

    [BsonElement("longUrl")]
    public string LongUrl { get; set; } = null!;

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;

    [BsonElement("clickCount")]
    public long ClickCount { get; set; } = 0;

    [BsonElement("expiresAt")]
    [BsonIgnoreIfNull]
    public DateTime? ExpiresAt { get; set; }

    [BsonElement("deletedAt")]
    [BsonIgnoreIfNull]
    public DateTime? DeletedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    public bool IsDeleted => DeletedAt.HasValue;
    public bool IsGone => IsExpired || IsDeleted;
}
