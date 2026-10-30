using Sufni.App.Shared.Models;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Text;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Segments.Services;

public class StravaSegmentServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T09:00:00Z");
    private static readonly DateTimeOffset SessionStart = DateTimeOffset.Parse("2026-01-01T10:00:00Z");

    private readonly InMemorySecureStorage storage = new();

    [Fact]
    public async Task GetConnectionSnapshotAsync_ReadsConnectionStoredBeforeTheFold()
    {
        await storage.SetStringAsync("SegmentAnalysis.Strava.ClientId", "client");
        await storage.SetStringAsync("SegmentAnalysis.Strava.ClientSecret", "secret");
        await storage.SetStringAsync("SegmentAnalysis.Strava.RefreshToken", "refresh");
        await storage.SetStringAsync("SegmentAnalysis.Strava.Scope", "read activity:read_all");
        var service = CreateService();

        var snapshot = await service.GetConnectionSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new StravaConnectionSnapshot("client", "secret", IsConnected: true), snapshot);
    }

    [Fact]
    public async Task ConnectAsync_StoresCredentialsAndTokens()
    {
        var service = CreateService(async request =>
        {
            var form = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("code=abc123", form);
            return TokenResponse("access", "refresh", "read,activity:read_all");
        });

        var result = await service.ConnectAsync(
            new StravaConnectRequest(" client ", " secret ", "http://localhost/exchange_token?code=abc123"),
            TestContext.Current.CancellationToken);

        Assert.IsType<StravaConnectResult.Connected>(result);
        Assert.Equal(
            new StravaConnectionSnapshot("client", "secret", IsConnected: true),
            await service.GetConnectionSnapshotAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectAsync_WithoutActivityScope_KeepsClientCredentialsOnly()
    {
        await storage.SetStringAsync("SegmentAnalysis.Strava.RefreshToken", "old-refresh");
        var service = CreateService(_ => Task.FromResult(TokenResponse("access", "refresh", "read")));

        var result = await service.ConnectAsync(
            new StravaConnectRequest("client", "secret", "http://localhost/exchange_token?code=abc123"),
            TestContext.Current.CancellationToken);

        Assert.IsType<StravaConnectResult.MissingRequiredScope>(result);
        Assert.Equal(
            new StravaConnectionSnapshot("client", "secret", IsConnected: false),
            await service.GetConnectionSnapshotAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindSegmentsAsync_WithoutConnection_ReturnsNotConnected()
    {
        var service = CreateService();

        var result = await service.FindSegmentsAsync(SessionStart, 3600, TestContext.Current.CancellationToken);

        Assert.IsType<StravaSegmentFetchResult.NotConnected>(result);
    }

    [Fact]
    public async Task FindSegmentsAsync_RefreshesTokenAndReturnsEffortsAsSessionRanges()
    {
        await StoreConnectionAsync(accessToken: null);
        var service = CreateService(
            _ => Task.FromResult(TokenResponse("fresh-access", "fresh-refresh", "read,activity:read_all")),
            request =>
            {
                Assert.Equal("fresh-access", request.Headers.Authorization?.Parameter);
                return Task.FromResult(JsonResponse("""
                    [{ "id": 42, "start_date": "2026-01-01T09:55:00Z", "elapsed_time": 4200 }]
                    """));
            },
            request =>
            {
                Assert.Equal(
                    "https://www.strava.com/api/v3/activities/42?include_all_efforts=true",
                    request.RequestUri?.ToString());
                return Task.FromResult(JsonResponse("""
                    {
                      "segment_efforts": [
                        {
                          "name": "Rock Garden",
                          "start_date": "2026-01-01T10:05:00Z",
                          "elapsed_time": 75,
                          "distance": 321.5
                        }
                      ]
                    }
                    """));
            });

        var result = await service.FindSegmentsAsync(SessionStart, 3600, TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<StravaSegmentFetchResult.Loaded>(result);
        var segment = Assert.Single(loaded.Segments);
        Assert.Equal("Rock Garden", segment.Name);
        Assert.Equal(321.5, segment.DistanceMeters);
        Assert.Equal(300, segment.AnalysisRange.StartSeconds);
        Assert.Equal(375, segment.AnalysisRange.EndSeconds);
    }

    [Fact]
    public async Task FindSegmentsAsync_WhenUnauthorized_RequiresReconnect()
    {
        await StoreConnectionAsync(accessToken: "access");
        var service = CreateService(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await service.FindSegmentsAsync(SessionStart, 3600, TestContext.Current.CancellationToken);

        Assert.IsType<StravaSegmentFetchResult.ReconnectRequired>(result);
        Assert.False((await service.GetConnectionSnapshotAsync(TestContext.Current.CancellationToken)).IsConnected);
    }

    [Fact]
    public async Task FindSegmentsAsync_WhenCancelled_Throws()
    {
        await StoreConnectionAsync(accessToken: "access");
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(JsonResponse("[]"));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.FindSegmentsAsync(SessionStart, 3600, cancellation.Token));
    }

    [Fact]
    public async Task ResetCredentialsAsync_DisconnectsAndDropsCachedResults()
    {
        await StoreConnectionAsync(accessToken: "access");
        var service = CreateService(_ => Task.FromResult(JsonResponse("[]")));
        Assert.IsType<StravaSegmentFetchResult.NoMatchingActivity>(
            await service.FindSegmentsAsync(SessionStart, 3600, TestContext.Current.CancellationToken));

        await service.ResetCredentialsAsync(TestContext.Current.CancellationToken);

        Assert.IsType<StravaSegmentFetchResult.NotConnected>(
            await service.FindSegmentsAsync(SessionStart, 3600, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CreateAuthorizationUrl_RequestsActivityScopeWithLocalRedirect()
    {
        var url = CreateService().CreateAuthorizationUrl("123");

        Assert.Contains("client_id=123", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%2Fexchange_token", url);
        Assert.Contains("scope=read%2Cactivity%3Aread_all", url);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectAsync_WhenResponseFails_PreservesExistingConnection(bool incompleteToken)
    {
        await StoreConnectionAsync("existing-access");
        var service = CreateService(_ => Task.FromResult(incompleteToken
            ? TokenResponse("", "", "read,activity:read_all")
            : new HttpResponseMessage(HttpStatusCode.BadRequest)));

        var result = await service.ConnectAsync(
            new StravaConnectRequest("new-client", "new-secret", "http://localhost/exchange_token?code=abc"),
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<StravaConnectResult.Failed>(result);
        Assert.Equal(StravaConnectFailureReason.Connect, failure.Reason);
        Assert.NotEmpty(failure.Details!);
        Assert.Equal(new StravaConnectionSnapshot("client", "secret", true),
            await service.GetConnectionSnapshotAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("access")]
    public async Task FindSegmentsAsync_WhenRequestFails_ReturnsFailureAndAllowsRetry(string? accessToken)
    {
        await StoreConnectionAsync(accessToken);
        var responses = new List<Func<HttpRequestMessage, Task<HttpResponseMessage>>>
        {
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        };
        if (accessToken is null)
            responses.Add(_ => Task.FromResult(TokenResponse("fresh", "refresh", "read,activity:read_all")));
        responses.Add(_ => Task.FromResult(JsonResponse("[]")));
        var service = CreateService(responses.ToArray());

        var failure = Assert.IsType<StravaSegmentFetchResult.Failed>(
            await service.FindSegmentsAsync(SessionStart, 3600, TestContext.Current.CancellationToken));
        Assert.Equal(StravaSegmentFetchFailureReason.Load, failure.Reason);
        Assert.NotEmpty(failure.Details!);
        Assert.True((await service.GetConnectionSnapshotAsync(TestContext.Current.CancellationToken)).IsConnected);
        Assert.IsType<StravaSegmentFetchResult.NoMatchingActivity>(
            await service.FindSegmentsAsync(SessionStart, 3600, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public async Task FindSegmentsAsync_WithoutUsableDuration_ReturnsMissingSessionTiming(double duration)
    {
        Assert.IsType<StravaSegmentFetchResult.MissingSessionTiming>(
            await CreateService().FindSegmentsAsync(SessionStart, duration, TestContext.Current.CancellationToken));
    }

    private StravaSegmentService CreateService(params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] responses)
    {
        var queue = new Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>>(responses);
        var handler = new StubHttpMessageHandler((request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return queue.Dequeue()(request);
        });
        return new StravaSegmentService(storage, new HttpClient(handler), new FakeTimeProvider(Now));
    }

    private async Task StoreConnectionAsync(string? accessToken)
    {
        await storage.SetStringAsync("SegmentAnalysis.Strava.ClientId", "client");
        await storage.SetStringAsync("SegmentAnalysis.Strava.ClientSecret", "secret");
        await storage.SetStringAsync("SegmentAnalysis.Strava.AccessToken", accessToken);
        await storage.SetStringAsync("SegmentAnalysis.Strava.RefreshToken", "refresh");
        await storage.SetStringAsync("SegmentAnalysis.Strava.AccessTokenExpiresAt", "2000000000");
        await storage.SetStringAsync("SegmentAnalysis.Strava.Scope", "read,activity:read_all");
    }

    private static HttpResponseMessage TokenResponse(string accessToken, string refreshToken, string scope) =>
        JsonResponse($$"""
            {
              "access_token": "{{accessToken}}",
              "refresh_token": "{{refreshToken}}",
              "expires_at": 2000000000,
              "scope": "{{scope}}"
            }
            """);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
