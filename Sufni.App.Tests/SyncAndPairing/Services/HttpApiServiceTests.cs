using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reactive.Linq;
using System.Security.Authentication;
using System.Threading;
using NSubstitute;
using Sufni.App.Infrastructure;

using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Sync;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class HttpApiServiceTests : IAsyncDisposable
{
    private readonly List<HttpApiService> services = [];
    private static readonly PeerEndpoint Endpoint = new("https://sync.example.test");
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    [Fact]
    public async Task PeerIdentity_UsesPinnedHubAndIssuedIncarnation_IndependentlyOfItsAddress()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetStringAsync("ServerUrl", "https://first.test");
        await storage.SetStringAsync("RefreshToken", "refresh");
        await storage.SetStringAsync("ServerCertificateThumbprint", "certificate");
        var incarnation = "database-one";
        var api = CreateApiService(storage, (_, _) => Task.FromResult(SyncTestServerHarness.Json(new TokenResponse(
            SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10)), "new-refresh", incarnation))));

        var service = await api.GetPeerAsync(Endpoint);
        Assert.Equal(new PeerIdentity("certificate", incarnation), await service.GetPeerIdentityAsync());
        Assert.Same(service, await api.GetPeerAsync(new PeerEndpoint("https://another-address.test")));
        Assert.Equal(new PeerIdentity("certificate", incarnation), await service.GetPeerIdentityAsync());
        incarnation = "database-after-reset";
        time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(new PeerIdentity("certificate", incarnation), await service.GetPeerIdentityAsync());
        Assert.Equal(incarnation, await storage.GetStringAsync("ServerDatabaseIncarnation"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, SynchronizationTransferStatus.Completed)]
    [InlineData(HttpStatusCode.Conflict, SynchronizationTransferStatus.Pending)]
    [InlineData(HttpStatusCode.NotFound, SynchronizationTransferStatus.Pending)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SynchronizationTransferStatus.Pending)]
    public async Task ItemTransfers_ClassifyPeerResponsesWithoutFailingTheRun(HttpStatusCode status, SynchronizationTransferStatus expected)
    {
        var accessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var service = await CreateService(CreateSecureStorage(), (request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == SynchronizationProtocol.EndpointPairRefresh
                ? SyncTestServerHarness.Json(new TokenResponse(accessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation))
                : new HttpResponseMessage(status)));
        var source = SyncTestServerHarness.ToPayload(SyncTestServerHarness.CreateRecordedSource());
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(expected, await service.PatchSessionPsstAsync(source.SessionId, [1, 2, 3], "target", token));
        Assert.Equal(expected, await service.PatchRecordedSessionSourceAsync(source, token));
        var blob = await service.GetSessionPsstAsync(source.SessionId, token);
        var recording = await service.GetRecordedSessionSourceAsync(source.SessionId, token);
        Assert.Equal(expected, blob.Status);
        Assert.Equal(expected, recording.Status);
        Assert.Null(blob.Payload);
        Assert.Null(recording.Payload);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UpgradeRequired)]
    public async Task ItemTransfer_AuthenticationAndProtocolErrorsStillFailTheRun(HttpStatusCode status)
    {
        var accessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var service = await CreateService(CreateSecureStorage(), (request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == SynchronizationProtocol.EndpointPairRefresh
                ? SyncTestServerHarness.Json(new TokenResponse(accessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation))
                : new HttpResponseMessage(status)));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.PatchSessionPsstAsync(Guid.NewGuid(), [1, 2, 3], "target", TestContext.Current.CancellationToken));

        Assert.Equal(status, failure.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ItemDownload_NetworkFailureOrTimeoutRemainsPending(bool timeout)
    {
        var accessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var service = await CreateService(CreateSecureStorage(), (request, _) =>
            request.RequestUri!.AbsolutePath == SynchronizationProtocol.EndpointPairRefresh
                ? Task.FromResult(SyncTestServerHarness.Json(new TokenResponse(accessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation)))
                : Task.FromException<HttpResponseMessage>(timeout ? new TaskCanceledException() : new HttpRequestException()));

        var result = await service.GetRecordedSessionSourceAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(SynchronizationTransferStatus.Pending, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void CreateHandler_AllowsTls12AndTls13()
    {
        using var handler = SynchronizationPeer.CreateHandler((_, _, _, _) => true);

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, handler.SslProtocols);
    }

    [Fact]
    public async Task GetIncompleteSessionIdsAsync_RefreshesAtThirtySecondBoundary()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var seenAuthorizations = new List<string?>();
        var refreshRequestCount = 0;
        var dataRequestCount = 0;

        var service = await CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    refreshRequestCount++;
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation));
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

        time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(29));
        await service.GetIncompleteSessionIdsAsync();
        Assert.Equal(1, refreshRequestCount);
        time.Advance(TimeSpan.FromSeconds(1));
        await service.GetIncompleteSessionIdsAsync();

        Assert.Equal(2, refreshRequestCount);
        Assert.Equal(4, dataRequestCount);
        Assert.All(seenAuthorizations, header => Assert.Equal(issuedAccessToken, header));
        await secureStorage.Received(2).SetStringAsync("RefreshToken", "refresh-2");
    }

    [Fact]
    public async Task Requests_IncludeProtocolSixHeader()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var seenProtocolHeaders = new List<string?>();

        var service = await CreateService(secureStorage, (request, _) =>
        {
            seenProtocolHeaders.Add(request.Headers.TryGetValues(
                    SynchronizationProtocol.SyncProtocolHeader,
                    out var values)
                ? Assert.Single(values)
                : null);

            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation)),
                SynchronizationProtocol.EndpointSessionIncomplete => SyncTestServerHarness.Json(new List<Guid>()),
                _ => throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}")
            });
        });

        await service.GetIncompleteSessionIdsAsync();

        Assert.Equal(["6", "6"], seenProtocolHeaders);
    }

    [Fact]
    public async Task UpgradeRequiredResponse_ThrowsSyncProtocolMismatchError()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var service = await CreateService(secureStorage, (request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation)),
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
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var sessionId = Guid.NewGuid();
        var service = await CreateService(secureStorage, (request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation)),
                var path when path == $"{SynchronizationProtocol.EndpointSessionData}{sessionId}" => SyncTestServerHarness.OctetStream([1, 2, 3], headers =>
                {
                    headers.TryAddWithoutValidation(SynchronizationProtocol.FingerprintHeader, "fp-1");
                }),
                _ => throw new InvalidOperationException($"Unexpected request path {request.RequestUri?.AbsolutePath}")
            });
        });

        var transfer = (await service.GetSessionPsstAsync(sessionId)).Payload;

        Assert.NotNull(transfer);
        Assert.Equal("fp-1", transfer!.Fingerprint);
        Assert.Equal([1, 2, 3], transfer.Data);
    }

    [Fact]
    public async Task PatchSessionPsstAsync_SendsOctetStreamBodyAndFingerprintHeader()
    {
        var secureStorage = CreateSecureStorage();
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var sessionId = Guid.NewGuid();
        byte[]? body = null;
        string? fingerprint = null;
        string? contentType = null;
        var service = await CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation));
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
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var sessionId = Guid.NewGuid();
        var service = await CreateService(secureStorage, (request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                SynchronizationProtocol.EndpointPairRefresh => SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation)),
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

        var source = (await service.GetRecordedSessionSourceAsync(sessionId)).Payload;

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
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var sessionId = Guid.NewGuid();
        byte[]? body = null;
        string? contentType = null;
        var seenHeaders = new Dictionary<string, string?>();
        var service = await CreateService(secureStorage, async (request, _) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation));
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
        var issuedAccessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var refreshRequestCount = 0;
        var dataRequestCount = 0;
        var seenAuthorizations = new List<string?>();
        var authorizationLock = new object();

        var service = await CreateService(secureStorage, async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    Interlocked.Increment(ref refreshRequestCount);
                    refreshStarted.TrySetResult();
                    await allowRefreshToComplete.Task.WaitAsync(cancellationToken);
                    return SyncTestServerHarness.Json(new TokenResponse(issuedAccessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation));
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

        var api = CreateApiService(secureStorage, async (request, _) =>
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
        var service = await api.GetPeerAsync(Endpoint);
        var pairedStates = new List<bool>();
        var unpaired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pairedStateSubscription = api.PeerChanges.Select(peer => peer is not null).Subscribe(value =>
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

        var service = await CreateService(secureStorage, async (request, cancellationToken) =>
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

        var service = CreateApiService(secureStorage, (_, _) =>
            throw new InvalidOperationException("Pairing probes should not use the stored endpoint."));

        await service.InitializeAsync();
        Assert.Equal(hasRefreshToken, service.PairedPeer is not null);
        Assert.Equal(hasRefreshToken, await ReadNextPairedStateAsync(service));
    }

    [Fact]
    public async Task ConfirmPairingAsync_PublishesTrue_AfterCredentialsAndCertificateAreStored()
    {
        var storage = new InMemorySecureStorage();
        using var certificate = CreateCertificate();
        var api = CreateApiService(storage, (request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == SynchronizationProtocol.EndpointPairRequest
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : SyncTestServerHarness.Json(new TokenResponse(
                    SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10)),
                    "new-refresh", "new-database"))), certificate);
        var states = new List<bool>();
        using var observation = api.PeerChanges.Select(peer => peer is not null).Subscribe(states.Add);

        await api.RequestPairingAsync(Endpoint, "device", "phone");
        await api.ConfirmPairingAsync("device", "phone", "123456");

        Assert.Equal([false, true], states);
        Assert.Equal("new-refresh", await storage.GetStringAsync("RefreshToken"));
        Assert.Equal(certificate.Thumbprint, await storage.GetStringAsync("ServerCertificateThumbprint"));
        Assert.Null(await storage.GetStringAsync("ServerUrl"));
    }

    [Fact]
    public async Task SessionDownloadCancellation_StopsTheHttpRequestAndKeepsPairing()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accessToken = SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10));
        var api = CreateApiService(CreateSecureStorage(), (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == SynchronizationProtocol.EndpointPairRefresh)
                return Task.FromResult(SyncTestServerHarness.Json(new TokenResponse(accessToken, "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation)));
            started.SetResult();
            return new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
        });
        var service = await api.GetPeerAsync(Endpoint);
        using var cancellation = new CancellationTokenSource();
        var download = service.GetSessionPsstAsync(Guid.NewGuid(), cancellation.Token);
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(api.PairedPeer);
    }

    [Fact]
    public async Task RunRequests_CarryDeclaredRunAndPhase_AndVerifyCurrentIncarnation()
    {
        var seen = new List<(string Path, string? Run, string? Phase)>();
        var peer = await CreateService(CreateSecureStorage(), async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            seen.Add((path, SyncTestServerHarness.Header(request, SynchronizationProtocol.RunIdHeader),
                SyncTestServerHarness.Header(request, SynchronizationProtocol.RunPhaseHeader)));
            if (path == SynchronizationProtocol.EndpointPairRefresh)
                return SyncTestServerHarness.Json(new TokenResponse(
                    SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10)), "refresh", "previous-db"));
            if (path == SynchronizationProtocol.EndpointRunStart)
            {
                var start = await SyncTestServerHarness.ReadJsonAsync<SynchronizationRunStart>(request);
                Assert.Equal(6, start!.Phases.Length);
                return SyncTestServerHarness.Json(new SynchronizationRunStarted("current-db"));
            }
            return path == SynchronizationProtocol.EndpointRunEnd
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : SyncTestServerHarness.Json(new List<Guid>());
        });
        var run = new SynchronizationRunStart(Guid.NewGuid(), "previous-db", SyncTestServerHarness.TransferPhases);
        var verified = await peer.BeginRunAsync(run, TestContext.Current.CancellationToken);
        Assert.Equal(new PeerIdentity("certificate", "current-db"), verified);
        await peer.BeginRunAsync(run with { DatabaseIncarnation = verified.DatabaseIncarnation }, TestContext.Current.CancellationToken);
        peer.SetRunPhase(SynchronizationPhase.PushingIncompleteSessions);
        await peer.GetIncompleteSessionIdsAsync(TestContext.Current.CancellationToken);
        await peer.CompleteRunAsync(TestContext.Current.CancellationToken);
        peer.ClearRun();

        Assert.All(seen.Where(value => value.Path == SynchronizationProtocol.EndpointRunStart), value => Assert.Null(value.Run));
        Assert.All(seen.Where(value => value.Path is SynchronizationProtocol.EndpointSessionIncomplete or SynchronizationProtocol.EndpointRunEnd), value =>
        {
            Assert.Equal(run.RunId.ToString(), value.Run);
            Assert.Equal(nameof(SynchronizationPhase.PushingIncompleteSessions), value.Phase);
        });
    }

    [Fact]
    public async Task Rediscovery_KeepsThePeerPin_AndRejectsAnotherHubAtTheNewAddress()
    {
        using var original = CreateCertificate();
        using var other = CreateCertificate();
        var presented = original;
        var storage = new InMemorySecureStorage();
        await storage.SetStringAsync("RefreshToken", "refresh");
        await storage.SetStringAsync("ServerCertificateThumbprint", original.Thumbprint);
        var acceptedHosts = new List<string>();
        var api = new HttpApiService(storage, validate => new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            if (!validate(request, presented, null, SslPolicyErrors.RemoteCertificateChainErrors))
                throw new HttpRequestException("Certificate rejected.");
            acceptedHosts.Add(request.RequestUri!.Host);
            return Task.FromResult(request.RequestUri.AbsolutePath == SynchronizationProtocol.EndpointPairRefresh
                ? SyncTestServerHarness.Json(new TokenResponse(
                    SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10)), "rotated", "db"))
                : SyncTestServerHarness.Json(new List<Guid>()));
        })), time);
        services.Add(api);
        var peer = await api.GetPeerAsync(Endpoint);
        await peer.GetIncompleteSessionIdsAsync(TestContext.Current.CancellationToken);

        Assert.Same(peer, await api.GetPeerAsync(new PeerEndpoint("https://rediscovered.test")));
        await peer.GetIncompleteSessionIdsAsync(TestContext.Current.CancellationToken);
        presented = other;
        await Assert.ThrowsAsync<HttpRequestException>(() => peer.GetIncompleteSessionIdsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(["sync.example.test", "sync.example.test", "rediscovered.test"], acceptedHosts);
        Assert.Equal(original.Thumbprint, await storage.GetStringAsync("ServerCertificateThumbprint"));
    }

    [Fact]
    public async Task RePair_WaitsForOldRotation_ThenKeepsOnlyNewCredentials()
    {
        using var certificate = CreateCertificate();
        var storage = new InMemorySecureStorage();
        await storage.SetStringAsync("RefreshToken", "old-refresh");
        await storage.SetStringAsync("ServerCertificateThumbprint", certificate.Thumbprint);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<string>();
        var api = CreateApiService(storage, async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(path);
            if (path == SynchronizationProtocol.EndpointPairRefresh)
            {
                refreshStarted.SetResult();
                await finishRefresh.Task.WaitAsync(token);
                return SyncTestServerHarness.Json(new TokenResponse(
                    SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10)), "old-rotated", "old-db"));
            }
            return path == SynchronizationProtocol.EndpointPairRequest
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : SyncTestServerHarness.Json(new TokenResponse(
                    SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10)), "new-refresh", "new-db"));
        }, certificate);
        var oldPeer = await api.GetPeerAsync(Endpoint);
        var oldRequest = oldPeer.GetIncompleteSessionIdsAsync(TestContext.Current.CancellationToken);
        await refreshStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var pairing = api.RequestPairingAsync(new PeerEndpoint("https://new.test"), "device", "phone");
        Assert.True(oldPeer.Lifetime.IsCancellationRequested);
        Assert.False(pairing.IsCompleted);
        finishRefresh.SetResult();
        await pairing.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldRequest);
        await api.ConfirmPairingAsync("device", "phone", "123456");
        var newPeer = await api.GetPeerAsync(new PeerEndpoint("https://new.test"));

        Assert.NotSame(oldPeer, newPeer);
        Assert.Equal("new-refresh", await storage.GetStringAsync("RefreshToken"));
        Assert.Equal(new PeerIdentity(certificate.Thumbprint, "new-db"), await newPeer.GetPeerIdentityAsync());
        Assert.DoesNotContain(SynchronizationProtocol.EndpointSessionIncomplete, requests);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldPeer.GetIncompleteSessionIdsAsync());
    }

    [Fact]
    public async Task Unpair_CancelsCapturedTransfers_AndClearsCredentialsBeforeRemoteRequest()
    {
        using var certificate = CreateCertificate();
        var storage = new InMemorySecureStorage();
        await storage.SetStringAsync("RefreshToken", "old-refresh");
        await storage.SetStringAsync("ServerCertificateThumbprint", certificate.Thumbprint);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = CreateApiService(storage, async (request, token) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case SynchronizationProtocol.EndpointPairRefresh:
                    return SyncTestServerHarness.Json(new TokenResponse(
                        SyncTestServerHarness.CreateAccessToken(time.GetUtcNow().AddMinutes(10)), "rotated", "db"));
                case SynchronizationProtocol.EndpointPairUnpair:
                    Assert.Null(await storage.GetStringAsync("RefreshToken"));
                    Assert.Null(await storage.GetStringAsync("ServerCertificateThumbprint"));
                    Assert.Equal("rotated", (await SyncTestServerHarness.ReadJsonAsync<UnpairRequest>(request))!.RefreshToken);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    started.SetResult();
                    return await new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
            }
        }, certificate);
        var peer = await api.GetPeerAsync(Endpoint);
        var download = peer.GetSessionPsstAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await api.UnpairAsync("device").AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Null(api.PairedPeer);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.GetPeerAsync(Endpoint));
    }

    private async Task<ISynchronizationPeer> CreateService(ISecureStorage storage,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        await CreateApiService(storage, sendAsync).GetPeerAsync(Endpoint);

    private HttpApiService CreateApiService(ISecureStorage storage,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        X509Certificate2? certificate = null)
    {
        var api = new HttpApiService(storage, validate => new HttpClient(new StubHttpMessageHandler((request, token) =>
        {
            if (certificate is not null && !validate(request, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors))
                throw new HttpRequestException("Certificate rejected.");
            return sendAsync(request, token);
        })), time);
        services.Add(api);
        return api;
    }

    private X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(SynchronizationProtocol.CertificateSubjectName, key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(time.GetUtcNow().AddDays(-1), time.GetUtcNow().AddDays(1));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in services) await service.DisposeAsync();
    }

    private static ISecureStorage CreateSecureStorage()
    {
        var secureStorage = Substitute.For<ISecureStorage>();
        secureStorage.GetStringAsync("ServerCertificateThumbprint").Returns("certificate");
        secureStorage.GetStringAsync("RefreshToken").Returns("refresh-1");
        return secureStorage;
    }

    private static async Task<bool> ReadNextPairedStateAsync(HttpApiService service)
    {
        var pairedState = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = service.PeerChanges.Select(peer => peer is not null).Subscribe(value => pairedState.TrySetResult(value));
        return await pairedState.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }
}
