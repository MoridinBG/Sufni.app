using Microsoft.Extensions.Time.Testing;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Coordinators;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.TestSupport.Sync;

internal sealed class SyncTestServerHarness : IDisposable
{
    public static JsonSerializerOptions CreateServerJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SynchronizationProtocol.ConfigureJson(options);
        return options;
    }

    public static readonly PeerIdentity Peer = new("hub-certificate", "hub-database");
    public SyncTestServerHarness()
    {
        SourceStore = new RecordedSessionSourceStore(publication);
        Inbound.Activity.Returns(InboundActivity);
        Pairing.States.Returns(PairingStates);
        Pairing.State.Returns(_ => PairingStates.Value);
        Pairing.ResolvePeerAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(_ => HttpApiService);
        SyncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>()).Returns(ApplyResult());
        SyncDataStore.GetSynchronizationDataAsync(Arg.Any<SynchronizationDirection>(), Arg.Any<long>(), Arg.Any<long>())
            .Returns(new SynchronizationData());
        HttpApiService.PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new SynchronizationData());
        SecureStorage.GetStringAsync("RefreshToken").Returns("refresh-1");
        SecureStorage.GetStringAsync("ServerCertificateThumbprint").Returns(Peer.CertificateThumbprint);
        HttpApiService.GetPeerIdentityAsync().Returns(Peer);
        HttpApiService.BeginRunAsync(Arg.Any<SynchronizationRunStart>(), Arg.Any<CancellationToken>())
            .Returns(call => Peer with { DatabaseIncarnation = call.Arg<SynchronizationRunStart>().DatabaseIncarnation });
        SyncDataStore.GetSequenceUpperBoundAsync(SynchronizationDirection.Push).Returns(9);
        HttpApiService.GetSessionPsstAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SynchronizationTransferResult<SessionBlobPayload>(null, SynchronizationTransferStatus.Pending));
        HttpApiService.GetRecordedSessionSourceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SynchronizationTransferResult<RecordedSessionSourcePayload>(null, SynchronizationTransferStatus.Pending));
        HttpApiService.GetIncompleteSessionIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        SessionRepository.GetIncompleteSessionIdsAsync().Returns([]);
        SessionRepository.GetIncompleteSessionIdsWithFingerprintAsync().Returns([]);
        HttpApiService.GetIncompleteSessionSourceIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        RecordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync().Returns([]);
        RecordedSessionSourceRepository.PutRecordedSessionSourceAsync(Arg.Any<RecordedSessionSource>())
            .Returns(call => RecordedSessionSourceSnapshot.From(call.Arg<RecordedSessionSource>()));
        RecordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([]);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    public ISecureStorage SecureStorage { get; } = Substitute.For<ISecureStorage>();
    public ISyncDataStore SyncDataStore { get; } = Substitute.For<ISyncDataStore>();
    public ISessionSyncApplier SessionSyncApplier { get; } = Substitute.For<ISessionSyncApplier>();
    public ISessionRepository SessionRepository { get; } = Substitute.For<ISessionRepository>();
    public IRecordedSessionSourceRepository RecordedSessionSourceRepository { get; } = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly List<SyncCoordinator> coordinators = [];
    public RecordedSessionSourceStore SourceStore { get; }
    public IInboundSyncCoordinator Inbound { get; } = Substitute.For<IInboundSyncCoordinator>();
    public BehaviorSubject<PeerSyncActivity> InboundActivity { get; } = new(new PeerSyncActivity.Idle());
    public static SynchronizationPhase[] TransferPhases =>
        [SynchronizationPhase.PushingLocalChanges, SynchronizationPhase.PullingRemoteChanges,
         SynchronizationPhase.PushingIncompleteSessions, SynchronizationPhase.PullingIncompleteSessions,
         SynchronizationPhase.PushingIncompleteSessionSources, SynchronizationPhase.PullingIncompleteSessionSources];
    public BehaviorSubject<PairingClientState> PairingStates { get; } = new(PairingClientState.Paired);
    public IPairingClientCoordinator Pairing { get; } = Substitute.For<IPairingClientCoordinator>();
    public IRecordedSessionSourceSyncQuery RecordedSessionSourceSyncQuery { get; } = Substitute.For<IRecordedSessionSourceSyncQuery>();
    public ISynchronizationPeer HttpApiService { get; } = Substitute.For<ISynchronizationPeer>();

    public HttpApiService CreateHttpApiService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        new(SecureStorage, _ => new HttpClient(new StubHttpMessageHandler(sendAsync)), Time);

    public SyncCoordinator CreateCoordinator(ISyncDataStore? dataStore = null,
        ISynchronizationPeer? peer = null, ISynchronizationApplier? applier = null,
        IUiThreadDispatcher? dispatcher = null, IBackgroundTaskRunner? runner = null,
        IInboundSyncCoordinator? inbound = null)
    {
        if (peer is not null)
            Pairing.ResolvePeerAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(peer);
        var store = dataStore ?? SyncDataStore;
        if (applier is null)
        {
            applier = Substitute.For<ISynchronizationApplier>();
            applier.ApplyAsync(Arg.Any<SynchronizationData>(), SynchronizationDirection.Pull)
                .Returns(call => store.ApplyRemoteSynchronizationDataAsync(call.Arg<SynchronizationData>()));
        }
        var coordinator = new SyncCoordinator(store, applier, SessionRepository, SessionSyncApplier,
            RecordedSessionSourceRepository, SourceStore, RecordedSessionSourceSyncQuery,
            runner ?? new InlineBackgroundTaskRunner(), dispatcher ?? new InlineUiThreadDispatcher(),
            Pairing, inbound);
        coordinators.Add(coordinator);
        return coordinator;
    }

    public static SynchronizationApplyResult ApplyResult(params SessionBlobSwap[] swaps) =>
        new([], [], [], [], [], [], [], swaps);

    public void Dispose()
    {
        foreach (var coordinator in coordinators) coordinator.Dispose();
        SourceStore.Dispose();
        publication.Dispose();
        PairingStates.Dispose();
        InboundActivity.Dispose();
    }

    public static string CreateAccessToken(DateTimeOffset expiresAt)
    {
        var token = new JwtSecurityToken(expires: expiresAt.UtcDateTime);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpResponseMessage Json<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload),
        };

    public static HttpResponseMessage OctetStream(byte[] payload, Action<HttpResponseHeaders>? configureHeaders = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(SynchronizationProtocol.OctetStreamContentType);
        configureHeaders?.Invoke(response.Headers);
        return response;
    }

    public static RecordedSessionSource CreateRecordedSource(byte[]? payload = null)
    {
        var data = RecordedSessionSourcePayloadCodec.CompressImportedSst(payload ?? [8, 6, 7, 5]);
        return new RecordedSessionSource
        {
            SessionId = Guid.NewGuid(),
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "sync.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(RecordedSessionSourceKind.ImportedSst, data),
            Payload = data
        };
    }

    public static RecordedSessionSourcePayload ToPayload(RecordedSessionSource source) => new(
        source.SessionId,
        source.SourceKind,
        source.SourceName,
        source.SchemaVersion,
        source.SourceHash,
        source.Payload);

    public static string? Header(HttpRequestMessage request, string headerName) =>
        request.Headers.TryGetValues(headerName, out var values) ? values.SingleOrDefault() : null;

    public static async Task<T?> ReadJsonAsync<T>(HttpRequestMessage request)
    {
        var json = await request.Content!.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, AppJson.Options);
    }
}

internal static class SyncRunObservation
{
    public static async Task<SynchronizationRunResult?> RunSyncAsync(this SyncCoordinator coordinator,
        IProgress<SynchronizationProgressSnapshot>? progress = null, CancellationToken cancellationToken = default)
    {
        SynchronizationRunResult? result = null;
        void Completed(object? _, SyncCompletedEventArgs args) => result = args.Result;
        void Failed(object? _, SyncFailedEventArgs args) => result = new SynchronizationRunResult.Failed(args.Reason, args.Details);
        using var observation = coordinator.States.Select(value => value.Progress).DistinctUntilChanged()
            .Subscribe(snapshot => { if (snapshot is not null) progress?.Report(snapshot); });
        coordinator.SyncCompleted += Completed;
        coordinator.SyncFailed += Failed;
        try { await coordinator.SyncAllAsync(cancellationToken); }
        finally
        {
            coordinator.SyncCompleted -= Completed;
            coordinator.SyncFailed -= Failed;
        }
        return result;
    }
}
