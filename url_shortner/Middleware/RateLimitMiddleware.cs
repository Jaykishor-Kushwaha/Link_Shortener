using System.Security.Claims;
using UrlShortener.Api.Configuration;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Rate limiting middleware using Redis fixed-window counters.
/// - GET /:code (redirect) → per-IP rate limit
/// - POST /urls (create)   → per-user rate limit
/// State is stored in Redis, so limits survive restarts and are correct across multiple app instances.
/// Returns 429 with Retry-After header when limit is exceeded.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;

    public RateLimitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, RedisService redis, AppSettings settings)
    {
        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        // Rate limit redirects: GET /:code (single segment, not /urls, /auth, /health, /swagger)
        if (method == "GET" && IsRedirectPath(path))
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var (allowed, retryAfter) = await redis.CheckRateLimitAsync(
                $"redirect:{ip}",
                settings.RateLimitRedirectMax,
                settings.RateLimitRedirectWindowSeconds);

            if (!allowed)
            {
                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = retryAfter.ToString();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($"{{\"error\":\"Rate limit exceeded. Try again in {retryAfter} seconds.\"}}");
                return;
            }
        }

        // Rate limit URL creation: POST /urls
        if (method == "POST" && path.Equals("/urls", StringComparison.OrdinalIgnoreCase))
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? context.User.FindFirstValue("sub");

            if (userId != null)
            {
                var (allowed, retryAfter) = await redis.CheckRateLimitAsync(
                    $"create:{userId}",
                    settings.RateLimitCreateMax,
                    settings.RateLimitCreateWindowSeconds);

                if (!allowed)
                {
                    context.Response.StatusCode = 429;
                    context.Response.Headers["Retry-After"] = retryAfter.ToString();
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"error\":\"Rate limit exceeded. Try again in {retryAfter} seconds.\"}}");
                    return;
                }
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Determines if a path is a redirect path (single segment, not a known API path).
    /// </summary>
    private static bool IsRedirectPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return false;

        // Known API prefixes that are NOT redirect paths
        if (path.StartsWith("/urls", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            return false;

        // Single segment path like /abc123
        var trimmed = path.TrimStart('/');
        return !trimmed.Contains('/');
    }
}
