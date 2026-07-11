using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Services;
using UrlShortener.Api.Validators;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("urls")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analyticsService;
    private readonly UrlService _urlService;

    public AnalyticsController(AnalyticsService analyticsService, UrlService urlService)
    {
        _analyticsService = analyticsService;
        _urlService = urlService;
    }

    /// <summary>
    /// GET /urls/:id/stats?from=&to=&bucket=hour|day — time series of click counts, owner-only.
    /// </summary>
    [HttpGet("{id}/stats")]
    public async Task<IActionResult> GetStats(
        string id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string bucket = "hour")
    {
        UrlValidator.ValidateBucket(bucket);
        await EnsureOwnershipAsync(id);

        var fromDate = from?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to?.ToUniversalTime() ?? DateTime.UtcNow;

        if (fromDate > toDate)
            throw new Models.AppException("'from' must be before 'to'.", 400);

        var timeSeries = await _analyticsService.GetTimeSeriesAsync(id, fromDate, toDate, bucket);
        return Ok(timeSeries);
    }

    /// <summary>
    /// GET /urls/:id/referrers?limit=10 — top referrers by click count, owner-only.
    /// </summary>
    [HttpGet("{id}/referrers")]
    public async Task<IActionResult> GetReferrers(string id, [FromQuery] int limit = 10)
    {
        await EnsureOwnershipAsync(id);

        var referrers = await _analyticsService.GetTopReferrersAsync(id, limit);
        return Ok(referrers);
    }

    /// <summary>
    /// Verifies that the authenticated user owns the URL.
    /// Returns 404 (not 403) for non-owned resources — this is intentional:
    /// returning 404 avoids leaking the existence of other users' links.
    /// </summary>
    private async Task EnsureOwnershipAsync(string urlId)
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                     ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? throw new Models.AppException("User ID not found in token.", 401);

        var url = await _urlService.GetByIdAsync(urlId);

        if (url == null || url.UserId != userId)
            throw new Models.AppException("URL not found.", 404);
    }
}
