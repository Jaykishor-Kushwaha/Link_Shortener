using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers;

/// <summary>
/// Redirect controller — the hot path. Separate from UrlController because this is public (no auth)
/// and has different rate limiting (per-IP vs per-user).
/// </summary>
[ApiController]
public class RedirectController : ControllerBase
{
    private readonly UrlService _urlService;

    public RedirectController(UrlService urlService)
    {
        _urlService = urlService;
    }

    /// <summary>
    /// GET /:code — redirects to the long URL. Records a click.
    /// Returns 410 Gone for expired or deleted links.
    /// Returns 404 for unknown codes.
    /// </summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> RedirectToLongUrl(string code)
    {
        // Validate code format — reject anything that looks like an API path
        if (string.IsNullOrEmpty(code) || code.Length > 32)
            return NotFound(new { error = "Short code not found." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var referrer = Request.Headers.Referer.FirstOrDefault() ?? string.Empty;
        var userAgent = Request.Headers.UserAgent.FirstOrDefault() ?? string.Empty;

        var (longUrl, isGone, _) = await _urlService.ResolveAsync(code, ip, referrer, userAgent);

        if (isGone)
            return StatusCode(410, new { error = "This link has expired or been deleted." });

        if (longUrl == null)
            return NotFound(new { error = "Short code not found." });

        return base.Redirect(longUrl);
    }
}
