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
using System.Threading.Tasks;

namespace Sufni.App.Services;

internal class HttpApiService : IHttpApiService
{
    private const string RefreshTokenKey = "RefreshToken";
    private const string ServerUrlKey = "ServerUrl";

    #region Public fields

    public string? ServerUrl { get; set; }

    #endregion

    #region Private fields

    private readonly HttpClient client = new(Handler);

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
    {
        this.secureStorage = secureStorage;
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

    private async Task RefreshTokensAsync()
    {
        await Initialization;

        Debug.Assert(ServerUrl is not null);
        Debug.Assert(refreshToken is not null);

        using var response = await client.PostAsJsonAsync(
            $"{ServerUrl}/pair/refresh",
            new RefreshRequest(refreshToken),
            AppJson.Context.RefreshRequest);

        // Clear out pairing information if we received a 401 - Unauthorized response before throwing an
        // exception for the not OK response.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            client.DefaultRequestHeaders.Authorization = null;
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

        await secureStorage.SetStringAsync(RefreshTokenKey, refreshToken);
    }

    private async Task Init()
    {
        ServerUrl = await secureStorage.GetStringAsync(ServerUrlKey);
        refreshToken = await secureStorage.GetStringAsync(RefreshTokenKey);
    }

    #endregion Private methods

    #region Public methods - pairing

    public async Task RequestPairingAsync(string url, string deviceId, string? displayName)
    {
        await Initialization;

        using var response = await client.PostAsJsonAsync(
            $"{url}{SynchronizationProtocol.EndpointPairRequest}",
            new PairingRequest(deviceId, displayName),
            AppJson.Context.PairingRequest);
        response.EnsureSuccessStatusCode();

        ServerUrl = url;
        await secureStorage.SetStringAsync(ServerUrlKey, ServerUrl);
    }

    public async Task ConfirmPairingAsync(string deviceId, string? displayName, string pin)
    {
        await Initialization;

        using var response = await client.PostAsJsonAsync(
            $"{ServerUrl}{SynchronizationProtocol.EndpointPairConfirm}",
            new PairingConfirm(deviceId, displayName, pin),
            AppJson.Context.PairingConfirm);
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync(AppJson.Context.TokenResponse);
        Debug.Assert(tokens != null);
        Debug.Assert(tokens.AccessToken != null);
        Debug.Assert(tokens.RefreshToken != null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        tokenExpiry = GetTokenExpiry(tokens.AccessToken);
        refreshToken = tokens.RefreshToken;

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

        var response = await client.PostAsJsonAsync(
            $"{ServerUrl}{SynchronizationProtocol.EndpointPairUnpair}",
            new UnpairRequest(deviceId, refreshToken),
            AppJson.Context.UnpairRequest);
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
            if (e.StatusCode == HttpStatusCode.Unauthorized) return false;
        }
        catch (Exception)
        {
            // If we get any other error than 401 - Unauthorized, we assume we are paired, because we have a refresh
            // token. This way, we won't show up as unpaired in case ofe.g. a network error.
            return true;
        }

        return true;
    }

    #endregion Public methods - pairing

    #region Public methods - syncing

    public async Task<SynchronizationData> PullSyncAsync(long since = 0)
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.GetAsync(
            $"{ServerUrl}{SynchronizationProtocol.EndpointSyncPull}?since={since}");
        response.EnsureSuccessStatusCode();
        var entities = await response.Content.ReadFromJsonAsync(AppJson.Context.SynchronizationData);
        Debug.Assert(entities != null);
        return entities;
    }

    public async Task PushSyncAsync(SynchronizationData syncData)
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.PutAsJsonAsync(
            $"{ServerUrl}{SynchronizationProtocol.EndpointSyncPush}",
            syncData,
            AppJson.Context.SynchronizationData);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Guid>> GetIncompleteSessionIdsAsync()
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.GetAsync(
            $"{ServerUrl}{SynchronizationProtocol.EndpointSessionIncomplete}");
        response.EnsureSuccessStatusCode();
        var incompleteSessions = await response.Content.ReadFromJsonAsync(AppJson.Context.ListGuid);
        return incompleteSessions ?? [];
    }

    public async Task<byte[]?> GetSessionPsstAsync(Guid id)
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.GetAsync($"{ServerUrl}{SynchronizationProtocol.EndpointSessionData}{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.PatchAsync(
            $"{ServerUrl}{SynchronizationProtocol.EndpointSessionData}{id}",
            new ByteArrayContent(data));
        response.EnsureSuccessStatusCode();
    }

    #endregion Public methods - syncing
}
