using System.Text.RegularExpressions;

namespace UrlShortener.Api.Configuration;

public class AppSettings
{
    public string MongoUri { get; set; } = "mongodb://localhost:27017/url_shortener";
    public string RedisUrl { get; set; } = "redis://localhost:6379";
    public int Port { get; set; } = 5000;
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string JwtSecret { get; set; } = "dev-jwt-secret-32-chars-long-ok!!";
    public string JwtRefreshSecret { get; set; } = "dev-refresh-secret-32-chars-ok!!";
    public string JwtAccessExpiry { get; set; } = "15m";
    public string JwtRefreshExpiry { get; set; } = "7d";
    public int RateLimitRedirectMax { get; set; } = 100;
    public int RateLimitRedirectWindowSeconds { get; set; } = 60;
    public int RateLimitCreateMax { get; set; } = 20;
    public int RateLimitCreateWindowSeconds { get; set; } = 60;
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// StackExchange.Redis expects "host:port", not "redis://host:port" or redis-cli commands.
    /// Converts standard redis:// URIs and redis-cli commands to StackExchange.Redis connection strings.
    /// </summary>
    public string RedisConnectionString
    {
        get
        {
            if (string.IsNullOrEmpty(RedisUrl)) return "localhost:6379";
            
            var cleanedUrl = RedisUrl.Trim();
            if (cleanedUrl.StartsWith("redis-cli -u ", StringComparison.OrdinalIgnoreCase))
            {
                cleanedUrl = cleanedUrl.Substring("redis-cli -u ".Length).Trim();
            }
            else if (cleanedUrl.StartsWith("redis-cli ", StringComparison.OrdinalIgnoreCase))
            {
                cleanedUrl = cleanedUrl.Substring("redis-cli ".Length).Trim();
            }

            if (cleanedUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
                cleanedUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
            {
                var useSsl = cleanedUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);
                try
                {
                    var uri = new Uri(cleanedUrl);
                    var host = uri.Host;
                    var port = uri.Port == -1 ? (useSsl ? 6380 : 6379) : uri.Port;
                    var userInfo = uri.UserInfo;
                    
                    var password = "";
                    if (!string.IsNullOrEmpty(userInfo))
                    {
                        var parts = userInfo.Split(':');
                        password = parts.Length > 1 ? parts[1] : parts[0];
                    }

                    var connectionString = $"{host}:{port}";
                    if (!string.IsNullOrEmpty(password))
                    {
                        connectionString += $",password={password}";
                    }
                    if (useSsl)
                    {
                        connectionString += ",ssl=true";
                    }
                    connectionString += ",abortConnect=false";
                    return connectionString;
                }
                catch
                {
                    return cleanedUrl;
                }
            }
            
            return cleanedUrl;
        }
    }

    public TimeSpan AccessTokenLifetime => ParseExpiry(JwtAccessExpiry);
    public TimeSpan RefreshTokenLifetime => ParseExpiry(JwtRefreshExpiry);

    public static TimeSpan ParseExpiry(string expiry)
    {
        var match = Regex.Match(expiry, @"^(\d+)([dhms])$");
        if (!match.Success) return TimeSpan.FromMinutes(15);

        var value = int.Parse(match.Groups[1].Value);
        return match.Groups[2].Value switch
        {
            "d" => TimeSpan.FromDays(value),
            "h" => TimeSpan.FromHours(value),
            "m" => TimeSpan.FromMinutes(value),
            "s" => TimeSpan.FromSeconds(value),
            _ => TimeSpan.FromMinutes(15)
        };
    }

    public static AppSettings FromEnvironment()
    {
        return new AppSettings
        {
            MongoUri = Env("MONGO_URI", "mongodb://localhost:27017/url_shortener"),
            RedisUrl = Env("REDIS_URL", "redis://localhost:6379"),
            Port = int.Parse(Env("PORT", "5000")),
            BaseUrl = Env("BASE_URL", "http://localhost:5000"),
            JwtSecret = Env("JWT_SECRET", "dev-jwt-secret-32-chars-long-ok!!"),
            JwtRefreshSecret = Env("JWT_REFRESH_SECRET", "dev-refresh-secret-32-chars-ok!!"),
            JwtAccessExpiry = Env("JWT_ACCESS_EXPIRY", "15m"),
            JwtRefreshExpiry = Env("JWT_REFRESH_EXPIRY", "7d"),
            RateLimitRedirectMax = int.Parse(Env("RATE_LIMIT_REDIRECT_MAX", "100")),
            RateLimitRedirectWindowSeconds = int.Parse(Env("RATE_LIMIT_REDIRECT_WINDOW_SECONDS", "60")),
            RateLimitCreateMax = int.Parse(Env("RATE_LIMIT_CREATE_MAX", "20")),
            RateLimitCreateWindowSeconds = int.Parse(Env("RATE_LIMIT_CREATE_WINDOW_SECONDS", "60")),
            CacheTtlSeconds = int.Parse(Env("CACHE_TTL_SECONDS", "300")),
        };
    }

    private static string Env(string key, string defaultValue) =>
        Environment.GetEnvironmentVariable(key) ?? defaultValue;
}
