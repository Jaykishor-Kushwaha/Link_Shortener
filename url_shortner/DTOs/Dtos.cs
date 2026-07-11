using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.DTOs;

// ── Auth DTOs ──

public class SignupRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = null!;

    [Required]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    [MaxLength(128)]
    public string Password { get; set; } = null!;
}

public class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;
}

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = null!;
}

public class TokenResponse
{
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
}

// ── URL DTOs ──

public class CreateUrlRequest
{
    [Required]
    [MaxLength(2048, ErrorMessage = "URL must be at most 2048 characters.")]
    public string LongUrl { get; set; } = null!;

    [MaxLength(32, ErrorMessage = "Custom alias must be at most 32 characters.")]
    [RegularExpression(@"^[A-Za-z0-9_-]+$", ErrorMessage = "Alias may only contain letters, digits, hyphens, and underscores.")]
    public string? CustomAlias { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

public class UpdateUrlRequest
{
    [Required]
    [MaxLength(2048, ErrorMessage = "URL must be at most 2048 characters.")]
    public string LongUrl { get; set; } = null!;
}

public class UrlResponse
{
    public string Id { get; set; } = null!;
    public string ShortCode { get; set; } = null!;
    public string ShortUrl { get; set; } = null!;
    public string LongUrl { get; set; } = null!;
    public long ClickCount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaginatedResponse<T>
{
    public List<T> Items { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// ── Analytics DTOs ──

public class TimeSeriesBucket
{
    public DateTime Bucket { get; set; }
    public long Count { get; set; }
}

public class ReferrerStat
{
    public string Referrer { get; set; } = null!;
    public long Count { get; set; }
}
