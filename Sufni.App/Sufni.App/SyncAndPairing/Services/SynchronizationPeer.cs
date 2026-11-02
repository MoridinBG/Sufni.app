using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Infrastructure;
namespace Sufni.App.SyncAndPairing.Services;

/// <summary>HTTP transport and credential rotation for one paired peer lifetime.</summary>
internal sealed class SynchronizationPeer : ISynchronizationPeer, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<SynchronizationPeer>();
    private readonly ISecureStorage secureStorage;
    private readonly TimeProvider timeProvider;
    private readonly HttpClient client;
    private readonly Action pairingExpired;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Threading.Lock refreshGate = new();
    private readonly System.Threading.Lock certificateGate = new();
    private Task? tokenRefresh;
    private string? certificateThumbprint;
    private string? refreshToken;
    private string? databaseIncarnation;
    private DateTimeOffset? tokenExpiry;
    private volatile AuthenticationHeaderValue? authorizationHeader;
    private PeerEndpoint? endpoint;
    private sealed record RunHeaders(Guid Id, SynchronizationPhase Phase);
    private RunHeaders? runHeaders;

    public CancellationToken Lifetime { get; }
    private PeerEndpoint Endpoint => Volatile.Read(ref endpoint) ?? throw CreateMissingCredentialsException();

    internal SynchronizationPeer(ISecureStorage secureStorage, TimeProvider timeProvider,
        PeerEndpoint? endpoint, string? certificateThumbprint, string? refreshToken,
        string? databaseIncarnation, Action pairingExpired,
        Func<Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>, HttpClient> createClient)
    {
        this.secureStorage = secureStorage;
        this.timeProvider = timeProvider;
        this.endpoint = endpoint;
        this.certificateThumbprint = certificateThumbprint;
        this.refreshToken = refreshToken;
        this.databaseIncarnation = databaseIncarnation;
        this.pairingExpired = pairingExpired;
        Lifetime = lifetime.Token;
        client = createClient(ValidateServerCertificate);
        ConfigureProtocolHeaders(client);
    }

    internal void UpdateEndpoint(PeerEndpoint value) => Volatile.Write(ref endpoint, value);

    private bool ValidateServerCertificate(HttpRequestMessage _, X509Certificate2? certificate,
        X509Chain? __, SslPolicyErrors ___)
    {
        lock (certificateGate)
        {
            if (!SynchronizationCertificateValidator.TryValidate(certificate, certificateThumbprint,
                    timeProvider.GetUtcNow(), out var observed)) return false;
            certificateThumbprint ??= observed;
            return true;
        }
    }

    internal async Task RequestPairingAsync(string deviceId, string? displayName)
    {
        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointPairRequest}";
        using var response = await SendWithLoggingAsync(HttpMethod.Post, route, () =>
            SendRequestAsync(HttpMethod.Post, route,
                JsonContent.Create(new PairingRequest(deviceId, displayName), AppJson.Context.PairingRequest)));
        response.EnsureSuccessStatusCode();
    }

    internal async Task ConfirmPairingAsync(string deviceId, string? displayName, string pin)
    {
        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointPairConfirm}";
        using var response = await SendWithLoggingAsync(HttpMethod.Post, route, () =>
            SendRequestAsync(HttpMethod.Post, route,
                JsonContent.Create(new PairingConfirm(deviceId, displayName, pin), AppJson.Context.PairingConfirm)));
        response.EnsureSuccessStatusCode();
        string thumbprint;
        lock (certificateGate) thumbprint = certificateThumbprint ?? throw CreateMissingCredentialsException();
        var tokens = await response.Content.ReadFromJsonAsync(AppJson.Context.TokenResponse);
        await SaveTokensAsync(tokens!);
        await secureStorage.SetStringAsync(HttpApiService.CertificateKey, thumbprint);
    }

    internal async Task UnpairAsync(string deviceId)
    {
        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointPairUnpair}";
        using var response = await SendWithLoggingAsync(HttpMethod.Post, route, () =>
            SendRequestAsync(HttpMethod.Post, route,
                JsonContent.Create(new UnpairRequest(deviceId, refreshToken ?? throw CreateMissingCredentialsException()),
                    AppJson.Context.UnpairRequest), finishAfterRetirement: true));
        response.EnsureSuccessStatusCode();
    }

    public async Task<PeerIdentity> GetPeerIdentityAsync()
    {
        await EnsureTokenFreshAsync();
        return new PeerIdentity(certificateThumbprint ?? throw CreateMissingCredentialsException(),
            databaseIncarnation ?? throw CreateMissingCredentialsException());
    }

    public async Task<PeerIdentity> BeginRunAsync(SynchronizationRunStart request, CancellationToken cancellationToken)
    {
        await EnsureTokenFreshAsync();
        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointRunStart}";
        using var response = await SendWithLoggingAsync(HttpMethod.Post, route, () =>
            SendRequestAsync(HttpMethod.Post, route, JsonContent.Create(request, AppJson.Context.SynchronizationRunStart),
                cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
        var started = await response.Content.ReadFromJsonAsync(AppJson.Context.SynchronizationRunStarted, cancellationToken);
        lock (refreshGate) databaseIncarnation = started!.DatabaseIncarnation;
        if (request.DatabaseIncarnation == started.DatabaseIncarnation)
            Volatile.Write(ref runHeaders, new RunHeaders(request.RunId, request.Phases[0]));
        return new PeerIdentity(certificateThumbprint!, started.DatabaseIncarnation);
    }

    public void SetRunPhase(SynchronizationPhase phase)
    {
        var current = Volatile.Read(ref runHeaders) ?? throw new InvalidOperationException("No synchronization run is active.");
        Volatile.Write(ref runHeaders, current with { Phase = phase });
    }

    public async Task CompleteRunAsync(CancellationToken cancellationToken)
    {
        await EnsureTokenFreshAsync();
        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointRunEnd}";
        using var response = await SendWithLoggingAsync(HttpMethod.Post, route, () =>
            SendRequestAsync(HttpMethod.Post, route, cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
    }

    public void ClearRun() => Volatile.Write(ref runHeaders, null);

    private async Task SaveTokensAsync(TokenResponse tokens)
    {
        await secureStorage.SetStringAsync(HttpApiService.RefreshTokenKey, tokens.RefreshToken);
        await secureStorage.SetStringAsync(HttpApiService.DatabaseIncarnationKey, tokens.DatabaseIncarnation);
        lock (refreshGate)
        {
            refreshToken = tokens.RefreshToken;
            databaseIncarnation = tokens.DatabaseIncarnation;
            tokenExpiry = GetTokenExpiry(tokens.AccessToken);
            authorizationHeader = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }
    }

    private async Task RefreshTokensAsync()
    {
        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointPairRefresh}";
        using var response = await SendWithLoggingAsync(HttpMethod.Post, route, () =>
            SendRequestAsync(HttpMethod.Post, route,
                JsonContent.Create(new RefreshRequest(refreshToken!), AppJson.Context.RefreshRequest),
                finishAfterRetirement: true));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.Warning("Clearing local pairing credentials after token refresh returned unauthorized");
            refreshToken = null;
            authorizationHeader = null;
            await HttpApiService.ClearCredentialsAsync(secureStorage);
            pairingExpired();
        }
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync(AppJson.Context.TokenResponse);
        await SaveTokensAsync(tokens!);
    }

    private async Task EnsureTokenFreshAsync()
    {
        Task refresh;
        lock (refreshGate)
        {
            Lifetime.ThrowIfCancellationRequested();
            if (refreshToken is null) throw CreateMissingCredentialsException();
            if (tokenExpiry > timeProvider.GetUtcNow().AddSeconds(30)) return;
            tokenRefresh ??= RefreshTokensAsync();
            refresh = tokenRefresh;
        }
        try { await refresh; }
        finally
        {
            lock (refreshGate)
                if (ReferenceEquals(tokenRefresh, refresh) && refresh.IsCompleted) tokenRefresh = null;
        }
        Lifetime.ThrowIfCancellationRequested();
    }

    internal async Task RetireAsync()
    {
        Task? refresh;
        lock (refreshGate)
        {
            lifetime.Cancel();
            refresh = tokenRefresh;
        }
        // Rotation must persist before another peer may write the shared secure keys.
        if (refresh is not null)
        {
            try { await refresh; }
            catch { /* The request awaiting this refresh owns its failure. */ }
        }
    }

    public void Dispose()
    {
        client.Dispose();
        lifetime.Dispose();
    }

    private static DateTimeOffset GetTokenExpiry(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var exp = jwt.Payload.Expiration ?? 0;
        return DateTimeOffset.FromUnixTimeSeconds(exp);
    }

    private static string GetRoute(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.PathAndQuery;
        }

        return url;
    }

    private static HttpRequestException CreateMissingCredentialsException() =>
        new("Synchronization pairing credentials are missing.", null, HttpStatusCode.Unauthorized);

    private static void ConfigureProtocolHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove(SynchronizationProtocol.SyncProtocolHeader);
        client.DefaultRequestHeaders.Add(
            SynchronizationProtocol.SyncProtocolHeader,
            SynchronizationProtocol.SyncProtocolVersion.ToString(CultureInfo.InvariantCulture));
    }

    // The Authorization value is attached per request instead of through
    // client.DefaultRequestHeaders: bounded-parallel transfers can be mid-send
    // while a token refresh completes, and the default-header collection is not
    // safe to mutate concurrently with sends. Swapping the field is an atomic
    // reference update, and a request racing the swap still carries the previous,
    // still-valid token (refresh runs 30 s before expiry).
    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        string url,
        HttpContent? content = null,
        Action<HttpRequestMessage>? configureRequest = null,
        CancellationToken cancellationToken = default,
        bool finishAfterRetirement = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            finishAfterRetirement ? CancellationToken.None : Lifetime);
        using var request = new HttpRequestMessage(method, url) { Content = content };
        var authorization = authorizationHeader;
        if (authorization is not null)
        {
            request.Headers.Authorization = authorization;
        }

        if (Volatile.Read(ref runHeaders) is { } run)
        {
            request.Headers.TryAddWithoutValidation(SynchronizationProtocol.RunIdHeader, run.Id.ToString());
            request.Headers.TryAddWithoutValidation(SynchronizationProtocol.RunPhaseHeader, run.Phase.ToString());
        }
        configureRequest?.Invoke(request);
        linked.Token.ThrowIfCancellationRequested();
        return await client.SendAsync(request, linked.Token);
    }

    private static ByteArrayContent CreateOctetStreamContent(byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(SynchronizationProtocol.OctetStreamContentType);
        return content;
    }

    private static string? GetResponseHeader(HttpResponseMessage response, string headerName)
    {
        if (!response.Headers.TryGetValues(headerName, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    private static string GetRequiredResponseHeader(HttpResponseMessage response, string headerName)
    {
        var value = GetResponseHeader(response, headerName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new HttpRequestException($"Sync response is missing required {headerName} header.");
        }

        return value;
    }

    internal static HttpClientHandler CreateHandler(
        Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> validateServerCertificate)
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            UseCookies = false,
        };
        handler.ServerCertificateCustomValidationCallback = validateServerCertificate;
        return handler;
    }

    private async Task<HttpResponseMessage> SendWithLoggingAsync(
        HttpMethod method,
        string url,
        Func<Task<HttpResponseMessage>> sendAsync)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = await sendAsync();

        logger.Verbose(
            "HTTP {Method} {Route} returned {StatusCode} in {DurationMs} ms",
            method.Method,
            GetRoute(url),
            (int)response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds);

        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            response.Dispose();
            throw new HttpRequestException(
                "Sync protocol version mismatch. Both devices must run the same app version.",
                null,
                HttpStatusCode.UpgradeRequired);
        }

        return response;
    }

    public async Task<SynchronizationData> PullSyncAsync(long since = 0, CancellationToken cancellationToken = default)
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSyncPull}?since={since}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => SendRequestAsync(HttpMethod.Get, route, cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
        var entities = await response.Content.ReadFromJsonAsync(AppJson.Context.SynchronizationData, cancellationToken);
        Debug.Assert(entities != null);

        logger.Verbose(
            "Pulled synchronization data with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, and {TrackCount} tracks",
            entities.Boards.Count,
            entities.Bikes.Count,
            entities.Setups.Count,
            entities.Sessions.Count,
            entities.Tracks.Count);

        return entities;
    }

    public async Task PushSyncAsync(SynchronizationData syncData, CancellationToken cancellationToken = default)
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        logger.Verbose(
            "Uploading synchronization data with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, and {TrackCount} tracks",
            syncData.Boards.Count,
            syncData.Bikes.Count,
            syncData.Setups.Count,
            syncData.Sessions.Count,
            syncData.Tracks.Count);

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSyncPush}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Put,
            route,
            () => SendRequestAsync(
                HttpMethod.Put,
                route,
                JsonContent.Create(syncData, AppJson.Context.SynchronizationData), cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Guid>> GetIncompleteSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSessionIncomplete}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => SendRequestAsync(HttpMethod.Get, route, cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
        var incompleteSessions = await response.Content.ReadFromJsonAsync(AppJson.Context.ListGuid, cancellationToken);

        logger.Verbose(
            "Received {IncompleteSessionCount} incomplete session ids from the server",
            incompleteSessions?.Count ?? 0);

        return incompleteSessions ?? [];
    }

    public Task<SynchronizationTransferResult<SessionBlobPayload>> GetSessionPsstAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransferAsync(id, async () =>
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSessionData}{id}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => SendRequestAsync(HttpMethod.Get, route, cancellationToken: cancellationToken));
        if (response.StatusCode == HttpStatusCode.NoContent)
            return new SynchronizationTransferResult<SessionBlobPayload>(null, SynchronizationTransferStatus.Completed);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            logger.Verbose("Session data was not found on the server for {SessionId}", id);
            return new SynchronizationTransferResult<SessionBlobPayload>(null, SynchronizationTransferStatus.Pending);
        }
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var transfer = new SessionBlobPayload(
            GetResponseHeader(response, SynchronizationProtocol.FingerprintHeader),
            data);

        logger.Verbose("Downloaded {ByteCount} bytes of session data for {SessionId}", transfer?.Data.Length ?? 0, id);

        return new SynchronizationTransferResult<SessionBlobPayload>(transfer, SynchronizationTransferStatus.Completed);
    }, new SynchronizationTransferResult<SessionBlobPayload>(null, SynchronizationTransferStatus.Pending), cancellationToken);

    public Task<SynchronizationTransferStatus> PatchSessionPsstAsync(Guid id, byte[] data, string? fingerprint, CancellationToken cancellationToken = default) =>
        TransferAsync(id, async () =>
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        logger.Verbose("Uploading {ByteCount} bytes of session data for {SessionId}", data.Length, id);

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSessionData}{id}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Patch,
            route,
            () => SendRequestAsync(
                HttpMethod.Patch,
                route,
                CreateOctetStreamContent(data),
                request =>
                {
                    if (fingerprint is not null)
                    {
                        request.Headers.TryAddWithoutValidation(SynchronizationProtocol.FingerprintHeader, fingerprint);
                    }
                }, cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
        return SynchronizationTransferStatus.Completed;
    }, SynchronizationTransferStatus.Pending, cancellationToken);

    public async Task<List<Guid>> GetIncompleteSessionSourceIdsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSessionSourceIncomplete}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => SendRequestAsync(HttpMethod.Get, route, cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
        var incompleteSources = await response.Content.ReadFromJsonAsync(AppJson.Context.ListGuid, cancellationToken);

        logger.Verbose(
            "Received {IncompleteSourceCount} incomplete recorded-source ids from the server",
            incompleteSources?.Count ?? 0);

        return incompleteSources ?? [];
    }

    public Task<SynchronizationTransferResult<RecordedSessionSourcePayload>> GetRecordedSessionSourceAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransferAsync(id, async () =>
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSessionSourceData}{id}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => SendRequestAsync(HttpMethod.Get, route, cancellationToken: cancellationToken));
        if (response.StatusCode == HttpStatusCode.NoContent)
            return new SynchronizationTransferResult<RecordedSessionSourcePayload>(null, SynchronizationTransferStatus.Completed);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            logger.Verbose("Recorded source was not found on the server for {SessionId}", id);
            return new SynchronizationTransferResult<RecordedSessionSourcePayload>(null, SynchronizationTransferStatus.Pending);
        }

        response.EnsureSuccessStatusCode();
        var sourceKindValue = GetRequiredResponseHeader(response, SynchronizationProtocol.SourceKindHeader);
        RecordedSessionSourceKind sourceKind;
        try
        {
            sourceKind = RecordedSessionSourceKindExtensions.FromStorageValue(sourceKindValue);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new HttpRequestException(
                $"Sync response contained unknown {SynchronizationProtocol.SourceKindHeader} header value.",
                ex);
        }

        var sourceName = Uri.UnescapeDataString(GetRequiredResponseHeader(response, SynchronizationProtocol.SourceNameHeader));
        var schemaVersionValue = GetRequiredResponseHeader(response, SynchronizationProtocol.SchemaVersionHeader);
        if (!int.TryParse(schemaVersionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var schemaVersion))
        {
            throw new HttpRequestException($"Sync response contained invalid {SynchronizationProtocol.SchemaVersionHeader} header value.");
        }

        var source = new RecordedSessionSourcePayload(
            id,
            sourceKind,
            sourceName,
            schemaVersion,
            GetRequiredResponseHeader(response, SynchronizationProtocol.SourceHashHeader),
            await response.Content.ReadAsByteArrayAsync(cancellationToken));

        logger.Verbose(
            "Downloaded recorded source for {SessionId} with {ByteCount} bytes",
            id,
            source?.Payload.Length ?? 0);

        return new SynchronizationTransferResult<RecordedSessionSourcePayload>(source, SynchronizationTransferStatus.Completed);
    }, new SynchronizationTransferResult<RecordedSessionSourcePayload>(null, SynchronizationTransferStatus.Pending), cancellationToken);

    public Task<SynchronizationTransferStatus> PatchRecordedSessionSourceAsync(RecordedSessionSourcePayload source, CancellationToken cancellationToken = default) =>
        TransferAsync(source.SessionId, async () =>
    {
        await EnsureTokenFreshAsync();
        cancellationToken.ThrowIfCancellationRequested();

        logger.Verbose("Uploading recorded source for {SessionId} with {ByteCount} bytes", source.SessionId, source.Payload.Length);

        var route = $"{Endpoint.Address}{SynchronizationProtocol.EndpointSessionSourceData}{source.SessionId}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Patch,
            route,
            () => SendRequestAsync(
                HttpMethod.Patch,
                route,
                CreateOctetStreamContent(source.Payload),
                request =>
                {
                    request.Headers.TryAddWithoutValidation(SynchronizationProtocol.SourceKindHeader, source.SourceKind.StorageValue);
                    request.Headers.TryAddWithoutValidation(SynchronizationProtocol.SourceNameHeader, Uri.EscapeDataString(source.SourceName));
                    request.Headers.TryAddWithoutValidation(SynchronizationProtocol.SchemaVersionHeader, source.SchemaVersion.ToString(CultureInfo.InvariantCulture));
                    request.Headers.TryAddWithoutValidation(SynchronizationProtocol.SourceHashHeader, source.SourceHash);
                }, cancellationToken: cancellationToken));
        response.EnsureSuccessStatusCode();
        return SynchronizationTransferStatus.Completed;
    }, SynchronizationTransferStatus.Pending, cancellationToken);

    private async Task<T> TransferAsync<T>(Guid id, Func<Task<T>> transfer, T pending, CancellationToken cancellationToken)
    {
        try { return await transfer(); }
        catch (HttpRequestException exception) when (exception.StatusCode is not
            (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.UpgradeRequired))
        {
            logger.Warning(exception, "Synchronization item {ItemId} remains pending after a transfer failure", id);
            return pending;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && !Lifetime.IsCancellationRequested)
        {
            logger.Warning(exception, "Synchronization item {ItemId} remains pending after a transfer timeout", id);
            return pending;
        }
    }

}
