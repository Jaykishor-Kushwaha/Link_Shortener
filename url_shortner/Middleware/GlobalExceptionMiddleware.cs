using System.Net;
using System.Text.Json;
using UrlShortener.Api.Models;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Global exception handler that converts AppException to structured JSON error responses
/// and catches unhandled exceptions as 500s. Ensures consistent error format across all endpoints.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning("Application error: {StatusCode} {Message}", ex.StatusCode, ex.Message);
            await WriteErrorResponse(context, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteErrorResponse(context, 500, "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var error = new { error = message };
        await context.Response.WriteAsync(JsonSerializer.Serialize(error));
    }
}
