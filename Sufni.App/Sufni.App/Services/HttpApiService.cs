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
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;

namespace Sufni.App.Services;

internal class HttpApiService : IHttpApiService
{
    private const string RefreshTokenKey = "RefreshToken";
    private const string ServerUrlKey = "ServerUrl";

    #region Private fields

    private readonly HttpClient client = new(Handler);

    private Task Initialization { get; }
    private ISecureStorage? secureStorage;
    private string? serverUrl;
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
            string.Equals(cert.Subject, SynchronizationServerService.CertificateSubjectName, StringComparison.OrdinalIgnoreCase)
    };

    #endregion

    #region Constructors

    public HttpApiService()
    {
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

        Debug.Assert(serverUrl is not null);
        Debug.Assert(refreshToken is not null);
        Debug.Assert(secureStorage is not null);

        using var response = await client.PostAsJsonAsync($"{serverUrl}/pair/refresh",
            new RefreshRequest(refreshToken));

        // Clear out pairing information if we received a 401 - Unauthorized response before throwing an
        // exception for the not OK response.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            client.DefaultRequestHeaders.Authorization = null;
            await secureStorage.RemoveAsync(RefreshTokenKey);
            await secureStorage.RemoveAsync(ServerUrlKey);
        }
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
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
        secureStorage = App.Current?.Services?.GetService<ISecureStorage>();
        Debug.Assert(secureStorage is not null);

        serverUrl = await secureStorage.GetStringAsync(ServerUrlKey);
        refreshToken = await secureStorage.GetStringAsync(RefreshTokenKey);
    }

    #endregion Private methods

    #region Public methods - pairing

    public async Task RequestPairingAsync(string url, string deviceId)
    {
        await Initialization;

        Debug.Assert(secureStorage is not null);

        using var response = await client.PostAsJsonAsync($"{url}/pair/request",
            new PairingRequest(deviceId));
        response.EnsureSuccessStatusCode();

        serverUrl = url;
        await secureStorage.SetStringAsync(ServerUrlKey, serverUrl);
    }

    public async Task ConfirmPairingAsync(string deviceId, string pin)
    {
        await Initialization;

        Debug.Assert(secureStorage is not null);

        using var response = await client.PostAsJsonAsync($"{serverUrl}/pair/confirm",
            new PairingConfirm(deviceId, pin));
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Debug.Assert(tokens != null);
        Debug.Assert(tokens.AccessToken != null);
        Debug.Assert(tokens.RefreshToken != null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        tokenExpiry = GetTokenExpiry(tokens.AccessToken);
        refreshToken = tokens.RefreshToken;

        await secureStorage.SetStringAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    public async Task UnpairAsync()
    {
        await Initialization;

        Debug.Assert(secureStorage is not null);

        var response = await client.DeleteAsync($"{serverUrl}/pair/unpair");
        response.EnsureSuccessStatusCode();
        
        client.DefaultRequestHeaders.Authorization = null;
        await secureStorage.RemoveAsync(RefreshTokenKey);
        await secureStorage.RemoveAsync(ServerUrlKey);
    }

    public async Task<bool> IsPairedAsync()
    {
        await Initialization;

        Debug.Assert(secureStorage is not null);

        var token =  await secureStorage.GetStringAsync(RefreshTokenKey);
        var url = await secureStorage.GetStringAsync(ServerUrlKey);
        if (token is null || url is null) return false;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        try
        {
            var response = await client.GetAsync($"{serverUrl}/pair/status");
            if (response.StatusCode == HttpStatusCode.Unauthorized) return false;
            response.EnsureSuccessStatusCode();
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

        using var response = await client.GetAsync($"{serverUrl}/sync/pull?since={since}");
        response.EnsureSuccessStatusCode();
        var entities = await response.Content.ReadFromJsonAsync<SynchronizationData>();
        Debug.Assert(entities != null);
        return entities;
    }

    public async Task PushSyncAsync(SynchronizationData syncData)
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.PutAsJsonAsync($"{serverUrl}/sync/push", syncData);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Guid>> GetIncompleteSessionIdsAsync()
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.GetAsync($"{serverUrl}/session/incomplete");
        response.EnsureSuccessStatusCode();
        var incompleteSessions = await response.Content.ReadFromJsonAsync<List<Guid>>();
        return incompleteSessions ?? [];
    }

    public async Task<byte[]?> GetSessionPsstAsync(Guid id)
    {
        await Initialization;

        if (tokenExpiry is null || tokenExpiry <= DateTimeOffset.Now.AddSeconds(30)) await RefreshTokensAsync();

        using var response = await client.GetAsync($"{serverUrl}/session/{id}/psst");
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

        using var response = await client.PatchAsync($"{serverUrl}/session/{id}/psst",
            new ByteArrayContent(data));
        response.EnsureSuccessStatusCode();
    }

    #endregion Public methods - syncing
}
