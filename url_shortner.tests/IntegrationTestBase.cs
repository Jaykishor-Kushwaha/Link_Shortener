using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.MongoDb;
using Testcontainers.Redis;
using UrlShortener.Api.Configuration;
using UrlShortener.Api.Infrastructure;
using Xunit;

namespace UrlShortener.Tests;

/// <summary>
/// Shared test fixture that spins up real MongoDB and Redis via Testcontainers,
/// creates a WebApplicationFactory against the real API, and provides helper methods
/// for authentication and HTTP calls.
/// </summary>
public class IntegrationTestBase : IAsyncLifetime
{
    protected readonly MongoDbContainer _mongoContainer;
    protected readonly RedisContainer _redisContainer;
    protected WebApplicationFactory<Program> _factory = null!;
    protected HttpClient _client = null!;

    public IntegrationTestBase()
    {
        _mongoContainer = new MongoDbBuilder()
            .WithImage("mongo:7")
            .Build();

        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        await _redisContainer.StartAsync();

        var mongoUri = _mongoContainer.GetConnectionString();
        if (mongoUri.Contains("?"))
        {
            mongoUri += "&directConnection=true";
        }
        else
        {
            mongoUri += "?directConnection=true";
        }
        var redisConnectionString = _redisContainer.GetConnectionString();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    // Replace AppSettings with test-specific values
                    services.Remove(services.First(d => d.ServiceType == typeof(AppSettings)));
                    var testSettings = new AppSettings
                    {
                        MongoUri = mongoUri,
                        RedisUrl = redisConnectionString,
                        Port = 5000,
                        BaseUrl = "http://localhost:5000",
                        JwtSecret = "test-jwt-secret-at-least-32-chars!!",
                        JwtRefreshSecret = "test-refresh-secret-at-least-32ch!!",
                        JwtAccessExpiry = "15m",
                        JwtRefreshExpiry = "7d",
                        RateLimitRedirectMax = 1000,  // Higher for tests
                        RateLimitRedirectWindowSeconds = 60,
                        RateLimitCreateMax = 1000,    // Higher for tests
                        RateLimitCreateWindowSeconds = 60,
                        CacheTtlSeconds = 300
                    };
                    services.AddSingleton(testSettings);

                    // Replace MongoDB context
                    services.Remove(services.First(d => d.ServiceType == typeof(MongoDbContext)));
                    var testMongoContext = new MongoDbContext(testSettings);
                    testMongoContext.CreateIndexesAsync().GetAwaiter().GetResult();
                    services.AddSingleton(testMongoContext);

                    // Replace Redis
                    services.Remove(services.First(d => d.ServiceType == typeof(IConnectionMultiplexer)));
                    var redis = ConnectionMultiplexer.Connect(testSettings.RedisConnectionString);
                    services.AddSingleton<IConnectionMultiplexer>(redis);
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false // Important: we want to test the 302, not follow it
        });
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory != null) await _factory.DisposeAsync();
        await _mongoContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    // ── Helpers ──

    protected async Task<(string AccessToken, string RefreshToken)> SignupAndLoginAsync(string email = "test@example.com", string password = "StrongP@ss123")
    {
        var signupResponse = await _client.PostAsJsonAsync("/auth/signup", new { email, password });
        if (signupResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Already exists, login instead
            var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email, password });
            loginResponse.EnsureSuccessStatusCode();
            var loginTokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
            return (loginTokens!.AccessToken, loginTokens.RefreshToken);
        }

        signupResponse.EnsureSuccessStatusCode();
        var tokens = await signupResponse.Content.ReadFromJsonAsync<TokenResponse>();
        return (tokens!.AccessToken, tokens.RefreshToken);
    }

    protected void SetAuth(string accessToken)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    protected void ClearAuth()
    {
        _client.DefaultRequestHeaders.Authorization = null;
    }

    private class TokenResponse
    {
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
    }
}
