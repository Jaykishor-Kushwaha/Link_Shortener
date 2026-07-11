using System.Net;
using System.Net.Sockets;

namespace UrlShortener.Api.Validators;

/// <summary>
/// Validates destination URLs to prevent open redirects and SSRF.
/// Rejects non-HTTP(S) schemes and destinations resolving to private/loopback/link-local addresses.
/// </summary>
public static class UrlValidator
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    /// <summary>
    /// Validates a URL is safe to store as a redirect destination.
    /// Throws AppException with a descriptive message on failure.
    /// </summary>
    public static async Task ValidateDestinationUrlAsync(string url)
    {
        // Basic parse check
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new Models.AppException("Invalid URL format.", 400);

        // Scheme check — block javascript:, data:, file:, ftp:, etc.
        if (!AllowedSchemes.Contains(uri.Scheme))
            throw new Models.AppException($"URL scheme '{uri.Scheme}' is not allowed. Only HTTP and HTTPS are accepted.", 400);

        // Host must exist
        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new Models.AppException("URL must include a valid host.", 400);

        // SSRF protection: resolve the hostname and reject private/loopback/link-local IPs
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);
            foreach (var addr in addresses)
            {
                if (IsPrivateOrReservedIp(addr))
                    throw new Models.AppException("URL destination resolves to a private or reserved IP address.", 400);
            }
        }
        catch (SocketException)
        {
            throw new Models.AppException("URL host could not be resolved.", 400);
        }
    }

    private static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 0.0.0.0
            if (bytes.All(b => b == 0)) return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 (loopback — already caught above, but explicit)
            if (ip.Equals(IPAddress.IPv6Loopback)) return true;
            // fc00::/7 (unique local addresses)
            if (bytes[0] == 0xfc || bytes[0] == 0xfd) return true;
            // fe80::/10 (link-local)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
            // :: (unspecified)
            if (ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any)) return true;
        }

        return false;
    }

    /// <summary>
    /// Validates analytics bucket parameter.
    /// </summary>
    public static void ValidateBucket(string? bucket)
    {
        if (string.IsNullOrEmpty(bucket)) return;
        if (bucket != "hour" && bucket != "day")
            throw new Models.AppException("Bucket must be 'hour' or 'day'.", 400);
    }
}
