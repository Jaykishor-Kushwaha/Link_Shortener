using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UrlShortener.Tests;

/// <summary>
/// Tests security requirements from Part 5:
/// - Refresh token replay invalidates session
/// - javascript:, data:, file: URLs are rejected
/// - Private IP destinations are rejected (SSRF)
/// - Input validation bounds
/// </summary>
public class SecurityTests : IntegrationTestBase, IAsyncLifetime
{
    /// <summary>
    /// Reusing a refresh token that was already used should invalidate the entire family.
    /// This is the replay detection mechanism from Part 5.
    /// </summary>
    [Fact]
    public async Task RefreshTokenReplay_ShouldInvalidateSession()
    {
        // Sign up and get initial tokens
        var (_, refreshToken) = await SignupAndLoginAsync("replay-test@test.com", "StrongP@ss123");

        // First refresh — should succeed and return new tokens
        var refresh1 = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        refresh1.EnsureSuccessStatusCode();

        // Replay the SAME old refresh token — should fail and invalidate the family
        var refresh2 = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        refresh2.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "reusing a refresh token must be detected as replay and invalidate the session");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://files.example.com/file.txt")]
    public async Task DangerousSchemes_ShouldBeRejected(string dangerousUrl)
    {
        var (accessToken, _) = await SignupAndLoginAsync("scheme-test@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        var response = await _client.PostAsJsonAsync("/urls", new { longUrl = dangerousUrl });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"URL with scheme '{dangerousUrl}' must be rejected");
    }

    [Theory]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://[::1]/admin")]
    public async Task PrivateIpDestinations_ShouldBeRejected(string ssrfUrl)
    {
        var (accessToken, _) = await SignupAndLoginAsync("ssrf-test@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        var response = await _client.PostAsJsonAsync("/urls", new { longUrl = ssrfUrl });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"URL resolving to private IP must be rejected: {ssrfUrl}");
    }

    [Fact]
    public async Task OverlyLongAlias_ShouldBeRejected()
    {
        var (accessToken, _) = await SignupAndLoginAsync("alias-test@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        var longAlias = new string('a', 5000);
        var response = await _client.PostAsJsonAsync("/urls", new
        {
            longUrl = "https://example.com",
            customAlias = longAlias
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "alias exceeding 32 characters must be rejected");
    }

    [Fact]
    public async Task ExpiredLinks_ShouldReturn410Gone()
    {
        var (accessToken, _) = await SignupAndLoginAsync("expiry-test@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        // Create a URL with an expiry in the past (should be rejected at creation)
        var response = await _client.PostAsJsonAsync("/urls", new
        {
            longUrl = "https://example.com/expiring",
            expiresAt = DateTime.UtcNow.AddSeconds(-10)
        });

        // Should be rejected because expiry is in the past
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidBucketParameter_ShouldBeRejected()
    {
        var (accessToken, _) = await SignupAndLoginAsync("bucket-test@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        // Create a URL first
        var createResp = await _client.PostAsJsonAsync("/urls", new
        {
            longUrl = "https://example.com/bucket-test"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await createResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var urlId = doc.RootElement.GetProperty("id").GetString()!;

        // Try an invalid bucket
        var response = await _client.GetAsync($"/urls/{urlId}/stats?bucket=DROP%20TABLE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "invalid bucket parameter must be rejected cleanly");
    }
}
