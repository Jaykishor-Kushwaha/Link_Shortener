using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("urls")]
[Authorize]
public class UrlController : ControllerBase
{
    private readonly UrlService _urlService;

    public UrlController(UrlService urlService)
    {
        _urlService = urlService;
    }

    /// <summary>
    /// POST /urls — creates a shortened URL. Optionally accepts custom alias and expiry.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUrlRequest request)
    {
        var userId = GetUserId();
        var result = await _urlService.CreateAsync(userId, request);
        return StatusCode(201, result);
    }

    /// <summary>
    /// GET /urls — the caller's links, paginated, with total click counts.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        var result = await _urlService.ListAsync(userId, page, pageSize);
        return Ok(result);
    }

    /// <summary>
    /// PATCH /urls/:id — updates the destination of a link the caller owns.
    /// Returns 404 if the link doesn't exist or belongs to another user.
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUrlRequest request)
    {
        var userId = GetUserId();
        var result = await _urlService.UpdateAsync(userId, id, request);
        if (result == null) return NotFound(new { error = "URL not found." });
        return Ok(result);
    }

    /// <summary>
    /// DELETE /urls/:id — soft-deletes a link the caller owns.
    /// Returns 404 if the link doesn't exist or belongs to another user.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var userId = GetUserId();
        var success = await _urlService.DeleteAsync(userId, id);
        if (!success) return NotFound(new { error = "URL not found." });
        return NoContent();
    }

    private string GetUserId()
    {
        return User.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? throw new Models.AppException("User ID not found in token.", 401);
    }
}
