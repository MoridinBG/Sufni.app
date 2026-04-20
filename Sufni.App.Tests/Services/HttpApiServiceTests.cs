using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class HttpApiServiceTests
{
    [Fact]
    public async Task GetIncompleteSessionIdsAsync_ReusesFreshTokenAcrossSequentialCalls()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var seenAuthorizations = new List<string?>();
        var refreshRequestCount = 0;
        var dataRequestCount = 0;

        var service = CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    refreshRequestCount++;
                    return CreateJsonResponse(new TokenResponse(issuedAccessToken, "refresh-2"));
                case SynchronizationProtocol.EndpointSessionIncomplete:
                    dataRequestCount++;
                    seenAuthorizations.Add(request.Headers.Authorization?.Parameter);
                    return CreateJsonResponse(new List<Guid> { Guid.NewGuid() });
                default:
                    throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}");
            }
        });

        await service.GetIncompleteSessionIdsAsync();
        await service.GetIncompleteSessionIdsAsync();

        Assert.Equal(1, refreshRequestCount);
        Assert.Equal(2, dataRequestCount);
        Assert.All(seenAuthorizations, header => Assert.Equal(issuedAccessToken, header));
        await secureStorage.Received(1).SetStringAsync("RefreshToken", "refresh-2");
    }

    [Fact]
    public async Task GetIncompleteSessionIdsAsync_CoalescesConcurrentRefreshes()
    {
        var secureStorage = CreateSecureStorage();
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefreshToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var issuedAccessToken = CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var refreshRequestCount = 0;
        var dataRequestCount = 0;
        var seenAuthorizations = new List<string?>();
        var authorizationLock = new object();

        var service = CreateService(secureStorage, async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    refreshRequestCount++;
                    refreshStarted.TrySetResult();
                    await allowRefreshToComplete.Task.WaitAsync(cancellationToken);
                    return CreateJsonResponse(new TokenResponse(issuedAccessToken, "refresh-2"));
                case SynchronizationProtocol.EndpointSessionIncomplete:
                    dataRequestCount++;
                    lock (authorizationLock)
                    {
                        seenAuthorizations.Add(request.Headers.Authorization?.Parameter);
                    }

                    return CreateJsonResponse(new List<Guid>());
                default:
                    throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}");
            }
        });

        var firstRequest = service.GetIncompleteSessionIdsAsync();
        var secondRequest = service.GetIncompleteSessionIdsAsync();

        await refreshStarted.Task;
        allowRefreshToComplete.TrySetResult();
        await Task.WhenAll(firstRequest, secondRequest);

        Assert.Equal(1, refreshRequestCount);
        Assert.Equal(2, dataRequestCount);
        Assert.All(seenAuthorizations, header => Assert.Equal(issuedAccessToken, header));
        await secureStorage.Received(1).SetStringAsync("RefreshToken", "refresh-2");
    }

    [Fact]
    public async Task GetIncompleteSessionIdsAsync_AfterUnauthorizedRefresh_FailsWithoutRetryingRefresh()
    {
        var secureStorage = CreateSecureStorage();
        var refreshRequestCount = 0;

        var service = CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    refreshRequestCount++;
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                case SynchronizationProtocol.EndpointSessionIncomplete:
                    throw new InvalidOperationException("Data requests should not run after an unauthorized refresh.");
                default:
                    throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}");
            }
        });

        var first = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetIncompleteSessionIdsAsync());
        var second = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetIncompleteSessionIdsAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(1, refreshRequestCount);
        await secureStorage.Received(1).RemoveAsync("RefreshToken");
        await secureStorage.Received(1).RemoveAsync("ServerUrl");
    }

    [Fact]
    public async Task GetIncompleteSessionIdsAsync_CoalescesConcurrentRefreshFailures()
    {
        var secureStorage = CreateSecureStorage();
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefreshToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshRequestCount = 0;

        var service = CreateService(secureStorage, async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    refreshRequestCount++;
                    refreshStarted.TrySetResult();
                    await allowRefreshToComplete.Task.WaitAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                case SynchronizationProtocol.EndpointSessionIncomplete:
                    throw new InvalidOperationException("Data requests should not run when refresh fails.");
                default:
                    throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}");
            }
        });

        var firstRequest = service.GetIncompleteSessionIdsAsync();
        var secondRequest = service.GetIncompleteSessionIdsAsync();

        await refreshStarted.Task;
        allowRefreshToComplete.TrySetResult();

        var first = await Assert.ThrowsAsync<HttpRequestException>(async () => await firstRequest);
        var second = await Assert.ThrowsAsync<HttpRequestException>(async () => await secondRequest);

        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);
        Assert.Equal(1, refreshRequestCount);
    }

    private static HttpApiService CreateService(
        ISecureStorage secureStorage,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        var client = new HttpClient(new StubHttpMessageHandler(sendAsync));
        return new HttpApiService(secureStorage, client);
    }

    private static ISecureStorage CreateSecureStorage()
    {
        var secureStorage = Substitute.For<ISecureStorage>();
        secureStorage.GetStringAsync("ServerUrl").Returns("https://sync.example.test");
        secureStorage.GetStringAsync("RefreshToken").Returns("refresh-1");
        return secureStorage;
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static string CreateAccessToken(DateTimeOffset expiresAt)
    {
        var token = new JwtSecurityToken(expires: expiresAt.UtcDateTime);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return sendAsync(request, cancellationToken);
        }
    }
}