using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace UrlShortener.Api.Models;

/// <summary>
/// Refresh token stored hashed in MongoDB.
/// Tokens in a rotation chain share a family UUID.
/// Reuse of an already-used token invalidates the entire family (replay detection).
/// TTL index on ExpiresAt auto-removes expired documents.
/// </summary>
public class RefreshToken
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tokenHash")]
    public string TokenHash { get; set; } = null!;

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;

    [BsonElement("family")]
    public string Family { get; set; } = null!;

    [BsonElement("used")]
    public bool Used { get; set; } = false;

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
