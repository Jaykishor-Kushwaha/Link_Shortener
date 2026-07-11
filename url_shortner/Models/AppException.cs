namespace UrlShortener.Api.Models;

/// <summary>
/// Application-level exception carrying an HTTP status code.
/// Caught by GlobalExceptionMiddleware and translated to a JSON error response.
/// </summary>
public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
