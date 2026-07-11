using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UrlShortener.Tests;

/// <summary>
/// Tests cache invalidation from Part 3:
/// - PATCH then redirect → new destination
/// - DELETE then redirect → 410 Gone
/// </summary>
public class CacheInvalidationTests : IntegrationTestBase, IAsyncLifetime
{
    /// <summary>
    /// After PATCHing a URL, the next redirect must serve the NEW destination.
    /// This validates that PATCH invalidates the Redis cache.
    /// </summary>
    [Fact]
    public async Task PatchUrl_ThenRedirect_ShouldServeNewDestination()
    {
        var (accessToken, _) = await SignupAndLoginAsync("cache-patch@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        // Create a URL
        var createResp = await _client.PostAsJsonAsync("/urls", new
        {
            longUrl = "https://example.com/original"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await createResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var shortCode = doc.RootElement.GetProperty("shortCode").GetString()!;
        var urlId = doc.RootElement.GetProperty("id").GetString()!;

        // Redirect to populate cache
        ClearAuth();
        var redirect1 = await _client.GetAsync($"/{shortCode}");
        redirect1.StatusCode.Should().Be(HttpStatusCode.Redirect);
        redirect1.Headers.Location!.ToString().Should().Be("https://example.com/original");

        // Patch the URL
        SetAuth(accessToken);
        var patchResp = await _client.PatchAsJsonAsync($"/urls/{urlId}", new
        {
            longUrl = "https://example.com/updated"
        });
        patchResp.EnsureSuccessStatusCode();

        // Redirect again — must serve the new destination, not the cached old one
        ClearAuth();
        var redirect2 = await _client.GetAsync($"/{shortCode}");
        redirect2.StatusCode.Should().Be(HttpStatusCode.Redirect);
        redirect2.Headers.Location!.ToString().Should().Be("https://example.com/updated",
            "cache must be invalidated on PATCH so the new destination is served immediately");
    }

    /// <summary>
    /// After DELETEing a URL, the next redirect must return 410 Gone.
    /// This validates that DELETE invalidates the Redis cache.
    /// </summary>
    [Fact]
    public async Task DeleteUrl_ThenRedirect_ShouldReturn410Gone()
    {
        var (accessToken, _) = await SignupAndLoginAsync("cache-delete@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        // Create a URL
        var createResp = await _client.PostAsJsonAsync("/urls", new
        {
            longUrl = "https://example.com/to-delete"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await createResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var shortCode = doc.RootElement.GetProperty("shortCode").GetString()!;
        var urlId = doc.RootElement.GetProperty("id").GetString()!;

        // Redirect to populate cache
        ClearAuth();
        var redirect1 = await _client.GetAsync($"/{shortCode}");
        redirect1.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // Delete the URL
        SetAuth(accessToken);
        var deleteResp = await _client.DeleteAsync($"/urls/{urlId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Redirect again — must return 410 Gone, not the cached destination
        ClearAuth();
        var redirect2 = await _client.GetAsync($"/{shortCode}");
        redirect2.StatusCode.Should().Be(HttpStatusCode.Gone,
            "deleted links must return 410, not serve from stale cache");
    }
}
