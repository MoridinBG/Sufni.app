using System;
using Sufni.App.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Sufni.App.Services;

internal class HttpApiService : IHttpApiService
{
    private static readonly ILogger logger = Log.ForContext<HttpApiService>();

    private const string RefreshTokenKey = "RefreshToken";
    private const string ServerUrlKey = "ServerUrl";

    #region Public fields

    public string? ServerUrl { get; set; }

    #endregion

    #region Private fields

    private readonly HttpClient client;
    private readonly SemaphoreSlim tokenRefreshGate = new(1, 1);
    private Task Initialization { get; }
    private readonly ISecureStorage secureStorage;
    private string? refreshToken;
    private DateTimeOffset? tokenExpiry;

    private static readonly HttpClientHandler Handler = new()
    {
        SslProtocols = SslProtocols.Tls13,
        UseCookies = false,

        // XXX: Checking only expiration and the CN. This is NOT secure, but we are on a home (local) network.
        // Ideal solution would be registration via QR codes, which would allow exchanging certificates too, but
        // there's no cross-platform Avalonia library for handling the camera.
        ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            cert is not null &&
            cert.NotAfter >= DateTimeOffset.Now &&
            string.Equals(cert.Subject, SynchronizationProtocol.CertificateSubjectName, StringComparison.OrdinalIgnoreCase)
    };

    #endregion

    #region Constructors

    public HttpApiService(ISecureStorage secureStorage)
        : this(secureStorage, new HttpClient(Handler))
    {
    }

    internal HttpApiService(ISecureStorage secureStorage, HttpClient client)
    {
        this.secureStorage = secureStorage;
        this.client = client;
        Initialization = Init();
    }

    #endregion Constructors

    #region Private methods

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

    private void EnsureRefreshCredentialsPresent()
    {
        if (ServerUrl is null || refreshToken is null)
        {
            throw CreateMissingCredentialsException();
        }
    }

    private async Task<HttpResponseMessage> SendWithLoggingAsync(
        HttpMethod method,
        string url,
        Func<Task<HttpResponseMessage>> sendAsync)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await sendAsync();

            logger.Verbose(
                "HTTP {Method} {Route} returned {StatusCode} in {DurationMs} ms",
                method.Method,
                GetRoute(url),
                (int)response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "HTTP {Method} {Route} failed after {DurationMs} ms",
                method.Method,
                GetRoute(url),
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private async Task RefreshTokensAsync()
    {
        await Initialization;

        EnsureRefreshCredentialsPresent();

        logger.Verbose("Refreshing synchronization tokens");

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointPairRefresh}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Post,
            route,
            () => client.PostAsJsonAsync(
                route,
                new RefreshRequest(refreshToken),
                AppJson.Context.RefreshRequest));

        // Clear out pairing information if we received a 401 - Unauthorized response before throwing an
        // exception for the not OK response.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.Warning("Clearing local pairing credentials after token refresh returned unauthorized");
            client.DefaultRequestHeaders.Authorization = null;
            ServerUrl = null;
            refreshToken = null;
            tokenExpiry = null;
            await secureStorage.RemoveAsync(RefreshTokenKey);
            await secureStorage.RemoveAsync(ServerUrlKey);
        }
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync(AppJson.Context.TokenResponse);
        Debug.Assert(tokens != null);
        Debug.Assert(tokens.AccessToken != null);
        Debug.Assert(tokens.RefreshToken != null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        tokenExpiry = GetTokenExpiry(tokens.AccessToken);
        refreshToken = tokens.RefreshToken;

        logger.Verbose("Synchronization tokens refreshed and expire at {TokenExpiry}", tokenExpiry);

        await secureStorage.SetStringAsync(RefreshTokenKey, refreshToken);
    }

    private bool IsTokenRefreshRequired() =>
        tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30);

    private async Task EnsureTokenFreshAsync()
    {
        await Initialization;

        EnsureRefreshCredentialsPresent();

        if (!IsTokenRefreshRequired())
        {
            return;
        }

        await tokenRefreshGate.WaitAsync();
        try
        {
            EnsureRefreshCredentialsPresent();

            if (!IsTokenRefreshRequired())
            {
                return;
            }

            await RefreshTokensAsync();
        }
        finally
        {
            tokenRefreshGate.Release();
        }
    }

    private async Task Init()
    {
        ServerUrl = await secureStorage.GetStringAsync(ServerUrlKey);
        refreshToken = await secureStorage.GetStringAsync(RefreshTokenKey);

        logger.Verbose(
            "HTTP API service initialized with stored server URL present {HasServerUrl} and refresh token present {HasRefreshToken}",
            ServerUrl is not null,
            refreshToken is not null);
    }

    #endregion Private methods

    #region Public methods - pairing

    public async Task RequestPairingAsync(string url, string deviceId, string? displayName)
    {
        await Initialization;

        var route = $"{url}{SynchronizationProtocol.EndpointPairRequest}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Post,
            route,
            () => client.PostAsJsonAsync(
                route,
                new PairingRequest(deviceId, displayName),
                AppJson.Context.PairingRequest));
        response.EnsureSuccessStatusCode();

        ServerUrl = url;
        await secureStorage.SetStringAsync(ServerUrlKey, ServerUrl);
    }

    public async Task ConfirmPairingAsync(string deviceId, string? displayName, string pin)
    {
        await Initialization;

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointPairConfirm}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Post,
            route,
            () => client.PostAsJsonAsync(
                route,
                new PairingConfirm(deviceId, displayName, pin),
                AppJson.Context.PairingConfirm));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync(AppJson.Context.TokenResponse);
        Debug.Assert(tokens != null);
        Debug.Assert(tokens.AccessToken != null);
        Debug.Assert(tokens.RefreshToken != null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        tokenExpiry = GetTokenExpiry(tokens.AccessToken);
        refreshToken = tokens.RefreshToken;

        logger.Verbose("Pairing confirmation stored refreshed credentials expiring at {TokenExpiry}", tokenExpiry);

        await secureStorage.SetStringAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    public async Task UnpairAsync(string deviceId)
    {
        await Initialization;

        Debug.Assert(refreshToken is not null);

        // Clean out locally first, so even if the server call fails for some reason (e.g. no network),
        // the client won't store the server URL and refresh token anymore.
        // TODO: defer failed deletions until next connection
        await secureStorage.RemoveAsync(RefreshTokenKey);
        await secureStorage.RemoveAsync(ServerUrlKey);
        client.DefaultRequestHeaders.Authorization = null;

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointPairUnpair}";
        var response = await SendWithLoggingAsync(
            HttpMethod.Post,
            route,
            () => client.PostAsJsonAsync(
                route,
                new UnpairRequest(deviceId, refreshToken),
                AppJson.Context.UnpairRequest));
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsPairedAsync()
    {
        await Initialization;

        var token = await secureStorage.GetStringAsync(RefreshTokenKey);
        var url = await secureStorage.GetStringAsync(ServerUrlKey);
        if (token is null || url is null) return false;

        try
        {
            await RefreshTokensAsync();
        }
        catch (HttpRequestException e)
        {
            if (e.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.Warning("Pairing probe reported unauthorized refresh token");
                return false;
            }
        }
        catch (Exception)
        {
            // If we get any other error than 401 - Unauthorized, we assume we are paired, because we have a refresh
            // token. This way, we won't show up as unpaired in case ofe.g. a network error.
            logger.Verbose("Pairing probe kept paired state after a non-auth refresh failure");
            return true;
        }

        return true;
    }

    #endregion Public methods - pairing

    #region Public methods - syncing

    public async Task<SynchronizationData> PullSyncAsync(long since = 0)
    {
        await EnsureTokenFreshAsync();

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointSyncPull}?since={since}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => client.GetAsync(route));
        response.EnsureSuccessStatusCode();
        var entities = await response.Content.ReadFromJsonAsync(AppJson.Context.SynchronizationData);
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

    public async Task PushSyncAsync(SynchronizationData syncData)
    {
        await EnsureTokenFreshAsync();

        logger.Verbose(
            "Uploading synchronization data with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, and {TrackCount} tracks",
            syncData.Boards.Count,
            syncData.Bikes.Count,
            syncData.Setups.Count,
            syncData.Sessions.Count,
            syncData.Tracks.Count);

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointSyncPush}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Put,
            route,
            () => client.PutAsJsonAsync(
                route,
                syncData,
                AppJson.Context.SynchronizationData));
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Guid>> GetIncompleteSessionIdsAsync()
    {
        await EnsureTokenFreshAsync();

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointSessionIncomplete}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => client.GetAsync(route));
        response.EnsureSuccessStatusCode();
        var incompleteSessions = await response.Content.ReadFromJsonAsync(AppJson.Context.ListGuid);

        logger.Verbose(
            "Received {IncompleteSessionCount} incomplete session ids from the server",
            incompleteSessions?.Count ?? 0);

        return incompleteSessions ?? [];
    }

    public async Task<byte[]?> GetSessionPsstAsync(Guid id)
    {
        await EnsureTokenFreshAsync();

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointSessionData}{id}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Get,
            route,
            () => client.GetAsync(route));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.Verbose("Session data was not found on the server for {SessionId}", id);
            return null;
        }
        response.EnsureSuccessStatusCode();
        var psst = await response.Content.ReadAsByteArrayAsync();

        logger.Verbose("Downloaded {ByteCount} bytes of session data for {SessionId}", psst.Length, id);

        return psst;
    }

    public async Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        await EnsureTokenFreshAsync();

        logger.Verbose("Uploading {ByteCount} bytes of session data for {SessionId}", data.Length, id);

        var route = $"{ServerUrl}{SynchronizationProtocol.EndpointSessionData}{id}";
        using var response = await SendWithLoggingAsync(
            HttpMethod.Patch,
            route,
            () => client.PatchAsync(
                route,
                new ByteArrayContent(data)));
        response.EnsureSuccessStatusCode();
    }

    #endregion Public methods - syncing
}
