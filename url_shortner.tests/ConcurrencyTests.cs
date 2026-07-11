using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UrlShortener.Tests;

/// <summary>
/// Tests the two hard concurrency guarantees from Part 2:
/// 1. 200 concurrent POST /urls → 200 distinct codes, 0 errors
/// 2. 500 concurrent GET /:code → click count is exactly 500
/// These tests run against real MongoDB and Redis via Testcontainers.
/// </summary>
public class ConcurrencyTests : IntegrationTestBase, IAsyncLifetime
{
    /// <summary>
    /// Fire 200 concurrent POST /urls and assert 200 unique short codes with zero errors.
    /// This validates that the unique index on shortCode prevents collisions
    /// even under heavy concurrent load.
    /// </summary>
    [Fact]
    public async Task Create200ConcurrentUrls_ShouldReturn200UniqueCodesWithZeroErrors()
    {
        var (accessToken, _) = await SignupAndLoginAsync("concurrency-create@test.com", "StrongP@ss123");

        const int concurrentRequests = 200;
        var tasks = new Task<HttpResponseMessage>[concurrentRequests];

        for (int i = 0; i < concurrentRequests; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                return await client.PostAsJsonAsync("/urls", new
                {
                    longUrl = $"https://example.com/page/{index}"
                });
            });
        }

        var responses = await Task.WhenAll(tasks);

        // Assert all succeeded
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        successCount.Should().Be(concurrentRequests, "every concurrent request should succeed");

        // Extract short codes and verify uniqueness
        var shortCodes = new HashSet<string>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var shortCode = doc.RootElement.GetProperty("shortCode").GetString();
            shortCode.Should().NotBeNullOrEmpty();
            shortCodes.Add(shortCode!).Should().BeTrue($"short code '{shortCode}' should be unique");
        }

        shortCodes.Count.Should().Be(concurrentRequests, "all codes must be distinct");
    }

    /// <summary>
    /// Fire 500 concurrent GET /:code requests at one short code
    /// and assert the click count is exactly 500.
    /// This validates that atomic $inc prevents lost click counts.
    /// </summary>
    [Fact]
    public async Task Fire500ConcurrentRedirects_ShouldHaveExactly500Clicks()
    {
        var (accessToken, _) = await SignupAndLoginAsync("concurrency-clicks@test.com", "StrongP@ss123");
        SetAuth(accessToken);

        // Create a URL
        var createResponse = await _client.PostAsJsonAsync("/urls", new
        {
            longUrl = "https://example.com/click-test"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        var createDoc = JsonDocument.Parse(createBody);
        var shortCode = createDoc.RootElement.GetProperty("shortCode").GetString()!;
        var urlId = createDoc.RootElement.GetProperty("id").GetString()!;

        const int concurrentRequests = 500;
        var tasks = new Task<HttpResponseMessage>[concurrentRequests];

        for (int i = 0; i < concurrentRequests; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });
                return await client.GetAsync($"/{shortCode}");
            });
        }

        await Task.WhenAll(tasks);

        // All should be redirects
        var redirectCount = tasks.Count(t => t.Result.StatusCode == HttpStatusCode.Redirect ||
                                              t.Result.StatusCode == HttpStatusCode.Moved);
        redirectCount.Should().Be(concurrentRequests);

        // Wait a moment for the background click event service to flush
        await Task.Delay(5000);

        // Check the click count via the list endpoint
        var listResponse = await _client.GetAsync("/urls?page=1&pageSize=100");
        listResponse.EnsureSuccessStatusCode();
        var listBody = await listResponse.Content.ReadAsStringAsync();
        var listDoc = JsonDocument.Parse(listBody);
        var items = listDoc.RootElement.GetProperty("items").EnumerateArray();
        var targetUrl = items.FirstOrDefault(i => i.GetProperty("id").GetString() == urlId);

        targetUrl.GetProperty("clickCount").GetInt64().Should().Be(concurrentRequests,
            "every concurrent redirect must be counted — no lost increments");
    }
}
