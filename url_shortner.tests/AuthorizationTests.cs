using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UrlShortener.Tests;

/// <summary>
/// Tests authorization from Part 5:
/// - User A cannot PATCH user B's links
/// - User A cannot DELETE user B's links
/// - User A cannot read user B's stats
/// - User A cannot read user B's referrers
/// All must fail with 404 (not 403, to avoid leaking link existence).
/// </summary>
public class AuthorizationTests : IntegrationTestBase, IAsyncLifetime
{
    private string _userAToken = null!;
    private string _userBToken = null!;
    private string _userBUrlId = null!;

    private async Task SetupUsersAndUrl()
    {
        // Create user A
        var (tokenA, _) = await SignupAndLoginAsync("userA@test.com", "StrongP@ss123");
        _userAToken = tokenA;

        // Create user B and a URL owned by user B
        var (tokenB, _) = await SignupAndLoginAsync("userB@test.com", "StrongP@ss456");
        _userBToken = tokenB;

        SetAuth(_userBToken);
        var createResp = await _client.PostAsJsonAsync("/urls", new
        {
            longUrl = "https://example.com/user-b-link"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await createResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        _userBUrlId = doc.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task UserA_CannotPatch_UserBLink()
    {
        await SetupUsersAndUrl();
        SetAuth(_userAToken);

        var response = await _client.PatchAsJsonAsync($"/urls/{_userBUrlId}", new
        {
            longUrl = "https://evil.com/hijacked"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "user A must not be able to update user B's link");
    }

    [Fact]
    public async Task UserA_CannotDelete_UserBLink()
    {
        await SetupUsersAndUrl();
        SetAuth(_userAToken);

        var response = await _client.DeleteAsync($"/urls/{_userBUrlId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "user A must not be able to delete user B's link");
    }

    [Fact]
    public async Task UserA_CannotReadStats_UserBLink()
    {
        await SetupUsersAndUrl();
        SetAuth(_userAToken);

        var response = await _client.GetAsync($"/urls/{_userBUrlId}/stats");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "user A must not be able to read user B's stats");
    }

    [Fact]
    public async Task UserA_CannotReadReferrers_UserBLink()
    {
        await SetupUsersAndUrl();
        SetAuth(_userAToken);

        var response = await _client.GetAsync($"/urls/{_userBUrlId}/referrers");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "user A must not be able to read user B's referrers");
    }

    [Fact]
    public async Task UnauthenticatedUser_CannotAccessProtectedEndpoints()
    {
        ClearAuth();

        var postResponse = await _client.PostAsJsonAsync("/urls", new { longUrl = "https://example.com" });
        postResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var getResponse = await _client.GetAsync("/urls");
        getResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
