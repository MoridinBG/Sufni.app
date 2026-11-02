using System;
using System.Net.Http;
using System.Net.Security;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Services;

/// <summary>Creates and retires HTTP clients with their pairing credentials.</summary>
internal sealed class HttpApiService : IHttpApiService, IAsyncDisposable
{
    internal const string RefreshTokenKey = "RefreshToken";
    internal const string DatabaseIncarnationKey = "ServerDatabaseIncarnation";
    internal const string CertificateKey = "ServerCertificateThumbprint";
    private readonly ISecureStorage secureStorage;
    private readonly TimeProvider timeProvider;
    private readonly Func<Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>, HttpClient> createClient;
    private readonly SemaphoreSlim lifecycle = new(1);
    private readonly BehaviorSubject<bool> pairedState = new(false);
    private readonly Task initialization;
    private SynchronizationPeer? peer;

    public IObservable<bool> PairedState => pairedState.DistinctUntilChanged();

    public HttpApiService(ISecureStorage secureStorage, TimeProvider timeProvider)
        : this(secureStorage, validate => new HttpClient(SynchronizationPeer.CreateHandler(validate)), timeProvider) { }

    internal HttpApiService(ISecureStorage secureStorage,
        Func<Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>, HttpClient> createClient,
        TimeProvider timeProvider)
    {
        this.secureStorage = secureStorage;
        this.timeProvider = timeProvider;
        this.createClient = createClient;
        initialization = InitializeAsync();
    }

    private SynchronizationPeer CreatePeer(PeerEndpoint? endpoint, string? certificate = null,
        string? refreshToken = null, string? incarnation = null) =>
        new(secureStorage, timeProvider, endpoint, certificate, refreshToken, incarnation,
            () => pairedState.OnNext(false), createClient);

    private async Task InitializeAsync()
    {
        var refreshToken = await secureStorage.GetStringAsync(RefreshTokenKey);
        var certificate = await secureStorage.GetStringAsync(CertificateKey);
        var incarnation = await secureStorage.GetStringAsync(DatabaseIncarnationKey);
        await secureStorage.RemoveAsync("ServerUrl");
        if (refreshToken is not null && certificate is not null)
        {
            peer = CreatePeer(null, certificate, refreshToken, incarnation);
            pairedState.OnNext(true);
        }
    }

    public async Task<bool> IsPairedAsync()
    {
        await initialization;
        return pairedState.Value;
    }

    public async Task<ISynchronizationPeer> GetPeerAsync(PeerEndpoint endpoint)
    {
        await initialization;
        await lifecycle.WaitAsync();
        try
        {
            if (!pairedState.Value || peer is null)
                throw new HttpRequestException("Synchronization pairing credentials are missing.", null, System.Net.HttpStatusCode.Unauthorized);
            peer.UpdateEndpoint(endpoint);
            return peer;
        }
        finally { lifecycle.Release(); }
    }

    public async Task RequestPairingAsync(PeerEndpoint endpoint, string deviceId, string? displayName)
    {
        await initialization;
        await lifecycle.WaitAsync();
        try
        {
            await RetirePeerAsync();
            await ClearCredentialsAsync(secureStorage);
            pairedState.OnNext(false);
            peer = CreatePeer(endpoint);
            await peer.RequestPairingAsync(deviceId, displayName);
        }
        finally { lifecycle.Release(); }
    }

    public async Task ConfirmPairingAsync(string deviceId, string? displayName, string pin)
    {
        await initialization;
        await lifecycle.WaitAsync();
        try
        {
            if (peer is null) throw new InvalidOperationException("Pairing has not been requested.");
            await peer.ConfirmPairingAsync(deviceId, displayName, pin);
            pairedState.OnNext(true);
        }
        finally { lifecycle.Release(); }
    }

    public async Task UnpairAsync(string deviceId)
    {
        await initialization;
        await lifecycle.WaitAsync();
        try
        {
            var previous = peer;
            peer = null;
            try
            {
                if (previous is not null) await previous.RetireAsync();
                await ClearCredentialsAsync(secureStorage);
                pairedState.OnNext(false);
                if (previous is not null) await previous.UnpairAsync(deviceId);
            }
            finally { previous?.Dispose(); }
        }
        finally { lifecycle.Release(); }
    }

    internal static async Task ClearCredentialsAsync(ISecureStorage storage)
    {
        await storage.RemoveAsync(RefreshTokenKey);
        await storage.RemoveAsync(DatabaseIncarnationKey);
        await storage.RemoveAsync(CertificateKey);
    }

    private async Task RetirePeerAsync()
    {
        if (peer is null) return;
        await peer.RetireAsync();
        peer.Dispose();
        peer = null;
    }

    public async ValueTask DisposeAsync()
    {
        await initialization;
        await lifecycle.WaitAsync();
        try { await RetirePeerAsync(); }
        finally { lifecycle.Release(); }
        lifecycle.Dispose();
        pairedState.Dispose();
    }
}
