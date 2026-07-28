using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Security.Authentication;
using System.Threading;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Sync;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class HttpApiServiceTests
{
    [Fact]
    public void CreateHandler_AllowsTls12AndTls13()
    {
        using var handler = HttpApiService.CreateHandler((_, _, _, _) => true);

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, handler.SslProtocols);
    }

    [Fact]
    public async Task GetIncompleteSessionIdsAsync_ReusesFreshTokenAcrossSequentialCalls()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var seenAuthorizations = new List<string?>();
        var refreshRequestCount = 0;
        var dataRequestCount = 0;

        var service = CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    refreshRequestCount++;
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2"));
                case SynchronizationProtocol.EndpointSessionIncomplete:
                    dataRequestCount++;
                    seenAuthorizations.Add(request.Headers.Authorization?.Parameter);
                    return SyncTestServerHarness.Json(new List<Guid> { Guid.NewGuid() });
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
    public async Task Requests_IncludeSyncProtocolHeader()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var seenProtocolHeaders = new List<string?>();

        var service = CreateService(secureStorage, (request, _) =>
        {
            seenProtocolHeaders.Add(request.Headers.TryGetValues(
                    SynchronizationProtocol.SyncProtocolHeader,
                    out var values)
                ? Assert.Single(values)
                : null);

            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2")),
                SynchronizationProtocol.EndpointSessionIncomplete => SyncTestServerHarness.Json(new List<Guid>()),
                _ => throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}")
            });
        });

        await service.GetIncompleteSessionIdsAsync();

        Assert.Equal(
            [SynchronizationProtocol.SyncProtocolVersion.ToString(CultureInfo.InvariantCulture),
                SynchronizationProtocol.SyncProtocolVersion.ToString(CultureInfo.InvariantCulture)],
            seenProtocolHeaders);
    }

    [Fact]
    public async Task UpgradeRequiredResponse_ThrowsSyncProtocolMismatchError()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var service = CreateService(secureStorage, (request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2")),
                SynchronizationProtocol.EndpointSessionIncomplete => new HttpResponseMessage(HttpStatusCode.UpgradeRequired),
                _ => throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}")
            });
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetIncompleteSessionIdsAsync());

        Assert.Equal(HttpStatusCode.UpgradeRequired, exception.StatusCode);
        Assert.Contains("same app version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSessionPsstAsync_ReadsOctetStreamBodyAndFingerprintHeader()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var sessionId = Guid.NewGuid();
        var service = CreateService(secureStorage, (request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2")),
                var path when path == $"{SynchronizationProtocol.EndpointSessionData}{sessionId}" => SyncTestServerHarness.OctetStream([1, 2, 3], headers =>
                {
                    headers.TryAddWithoutValidation(SynchronizationProtocol.FingerprintHeader, "fp-1");
                }),
                _ => throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}")
            });
        });

        var transfer = await service.GetSessionPsstAsync(sessionId);

        Assert.NotNull(transfer);
        Assert.Equal("fp-1", transfer!.Fingerprint);
        Assert.Equal([1, 2, 3], transfer.Data);
    }

    [Fact]
    public async Task PatchSessionPsstAsync_SendsOctetStreamBodyAndFingerprintHeader()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var sessionId = Guid.NewGuid();
        byte[]? body = null;
        string? fingerprint = null;
        string? contentType = null;
        var service = CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2"));
                case var path when path == $"{SynchronizationProtocol.EndpointSessionData}{sessionId}":
                    Assert.Equal(HttpMethod.Patch, request.Method);
                    body = await request.Content!.ReadAsByteArrayAsync();
                    contentType = request.Content.Headers.ContentType?.MediaType;
                    fingerprint = request.Headers.TryGetValues(SynchronizationProtocol.FingerprintHeader, out var values)
                        ? Assert.Single(values)
                        : null;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}");
            }
        });

        await service.PatchSessionPsstAsync(sessionId, [4, 5, 6], "fp-2");

        Assert.Equal([4, 5, 6], body);
        Assert.Equal(SynchronizationProtocol.OctetStreamContentType, contentType);
        Assert.Equal("fp-2", fingerprint);
    }

    [Fact]
    public async Task GetRecordedSessionSourceAsync_ReadsOctetStreamBodyAndMetadataHeaders()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var sessionId = Guid.NewGuid();
        var service = CreateService(secureStorage, (request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2")),
                var path when path == $"{SynchronizationProtocol.EndpointSessionSourceData}{sessionId}" => SyncTestServerHarness.OctetStream([7, 8, 9], headers =>
                {
                    headers.TryAddWithoutValidation(SynchronizationProtocol.SourceKindHeader, RecordedSessionSourceKind.ImportedSst.StorageValue);
                    headers.TryAddWithoutValidation(SynchronizationProtocol.SourceNameHeader, Uri.EscapeDataString("ride data.sst"));
                    headers.TryAddWithoutValidation(SynchronizationProtocol.SchemaVersionHeader, "4");
                    headers.TryAddWithoutValidation(SynchronizationProtocol.SourceHashHeader, "hash-1");
                }),
                _ => throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}")
            });
        });

        var source = await service.GetRecordedSessionSourceAsync(sessionId);

        Assert.NotNull(source);
        Assert.Equal(sessionId, source!.SessionId);
        Assert.Equal(RecordedSessionSourceKind.ImportedSst, source.SourceKind);
        Assert.Equal("ride data.sst", source.SourceName);
        Assert.Equal(4, source.SchemaVersion);
        Assert.Equal("hash-1", source.SourceHash);
        Assert.Equal([7, 8, 9], source.Payload);
    }

    [Fact]
    public async Task PatchRecordedSessionSourceAsync_SendsOctetStreamBodyAndMetadataHeaders()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var sessionId = Guid.NewGuid();
        byte[]? body = null;
        string? contentType = null;
        var seenHeaders = new Dictionary<string, string?>();
        var service = CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2"));
                case var path when path == $"{SynchronizationProtocol.EndpointSessionSourceData}{sessionId}":
                    Assert.Equal(HttpMethod.Patch, request.Method);
                    body = await request.Content!.ReadAsByteArrayAsync();
                    contentType = request.Content.Headers.ContentType?.MediaType;
                    seenHeaders[SynchronizationProtocol.SourceKindHeader] = SyncTestServerHarness.Header(request, SynchronizationProtocol.SourceKindHeader);
                    seenHeaders[SynchronizationProtocol.SourceNameHeader] = SyncTestServerHarness.Header(request, SynchronizationProtocol.SourceNameHeader);
                    seenHeaders[SynchronizationProtocol.SchemaVersionHeader] = SyncTestServerHarness.Header(request, SynchronizationProtocol.SchemaVersionHeader);
                    seenHeaders[SynchronizationProtocol.SourceHashHeader] = SyncTestServerHarness.Header(request, SynchronizationProtocol.SourceHashHeader);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}");
            }
        });

        await service.PatchRecordedSessionSourceAsync(new RecordedSessionSourcePayload(
            sessionId,
            RecordedSessionSourceKind.LiveCapture,
            "ride data.sst",
            5,
            "hash-2",
            [10, 11, 12]));

        Assert.Equal([10, 11, 12], body);
        Assert.Equal(SynchronizationProtocol.OctetStreamContentType, contentType);
        Assert.Equal(RecordedSessionSourceKind.LiveCapture.StorageValue, seenHeaders[SynchronizationProtocol.SourceKindHeader]);
        Assert.Equal(Uri.EscapeDataString("ride data.sst"), seenHeaders[SynchronizationProtocol.SourceNameHeader]);
        Assert.Equal("5", seenHeaders[SynchronizationProtocol.SchemaVersionHeader]);
        Assert.Equal("hash-2", seenHeaders[SynchronizationProtocol.SourceHashHeader]);
    }

    [Fact]
    public async Task GetIncompleteSessionIdsAsync_CoalescesConcurrentRefreshes()
    {
        var secureStorage = CreateSecureStorage();
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefreshToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var refreshRequestCount = 0;
        var dataRequestCount = 0;
        var seenAuthorizations = new List<string?>();
        var authorizationLock = new object();

        var service = CreateService(secureStorage, async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    Interlocked.Increment(ref refreshRequestCount);
                    refreshStarted.TrySetResult();
                    await allowRefreshToComplete.Task.WaitAsync(cancellationToken);
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2"));
                case SynchronizationProtocol.EndpointSessionIncomplete:
                    Interlocked.Increment(ref dataRequestCount);
                    lock (authorizationLock)
                    {
                        seenAuthorizations.Add(request.Headers.Authorization?.Parameter);
                    }

                    return SyncTestServerHarness.Json(new List<Guid>());
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
        var pairedStates = new List<bool>();
        var unpaired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pairedStateSubscription = service.PairedState.Subscribe(value =>
        {
            pairedStates.Add(value);
            if (!value)
            {
                unpaired.TrySetResult();
            }
        });

        var first = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetIncompleteSessionIdsAsync());
        await unpaired.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        var second = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetIncompleteSessionIdsAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(1, refreshRequestCount);
        Assert.Equal([true, false], pairedStates);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PairingStateProbes_ReflectStoredRefreshToken(bool hasRefreshToken)
    {
        var secureStorage = CreateSecureStorage();
        if (!hasRefreshToken)
        {
            secureStorage.GetStringAsync("RefreshToken").Returns((string?)null);
        }

        var service = CreateService(secureStorage, (_, _) =>
            throw new InvalidOperationException("Pairing probes should not use the stored endpoint."));

        Assert.Equal(hasRefreshToken, await service.IsPairedAsync());
        Assert.Equal(hasRefreshToken, await ReadNextPairedStateAsync(service));
    }

    [Fact]
    public async Task ConfirmPairingAsync_PublishesTrue_AfterCredentialsAreStored()
    {
        var secureStorage = CreateSecureStorage();
        secureStorage.GetStringAsync("RefreshToken").Returns((string?)null);
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(DateTimeOffset.UtcNow.AddMinutes(10));
        var service = CreateService(secureStorage, (request, _) =>
        {
            Assert.Equal(SynchronizationProtocol.EndpointPairConfirm, request.RequestUri?.AbsolutePath);
            return Task.FromResult<HttpResponseMessage>(
                SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2")));
        });
        var pairedStates = new List<bool>();
        var paired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pairedStateSubscription = service.PairedState.Subscribe(value =>
        {
            pairedStates.Add(value);
            if (value)
            {
                paired.TrySetResult();
            }
        });

        await service.ConfirmPairingAsync("device-1", "phone", "123456");
        await paired.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([false, true], pairedStates);
        await secureStorage.Received(1).SetStringAsync("RefreshToken", "refresh-2");
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

    private static async Task<bool> ReadNextPairedStateAsync(HttpApiService service)
    {
        var pairedState = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = service.PairedState.Subscribe(value => pairedState.TrySetResult(value));
        return await pairedState.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
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
