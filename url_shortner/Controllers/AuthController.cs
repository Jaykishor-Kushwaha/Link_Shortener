using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// POST /auth/signup — email + password. Returns access and refresh tokens.
    /// </summary>
    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] SignupRequest request)
    {
        var tokens = await _authService.SignupAsync(request);
        return StatusCode(201, tokens);
    }

    /// <summary>
    /// POST /auth/login — returns access and refresh tokens.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var tokens = await _authService.LoginAsync(request);
        return Ok(tokens);
    }

    /// <summary>
    /// POST /auth/refresh — rotates the refresh token. Reuse of an old token invalidates the session.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var tokens = await _authService.RefreshAsync(request);
        return Ok(tokens);
    }
}
