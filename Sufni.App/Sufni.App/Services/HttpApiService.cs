using System;
using Sufni.App.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sufni.App.Services;

internal class HttpApiService : IHttpApiService
{
    #region Private fields

    private string? serverUrl;
    private readonly HttpClient client = new(Handler);

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

    private record PutResponse(
        [property: JsonPropertyName("id")] Guid Id);

    public async Task<string> RefreshTokensAsync(string url, string refreshToken)
    {
        serverUrl = url;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshToken);
        using HttpResponseMessage response = await client.PostAsync($"{serverUrl}/auth/refresh", null);

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        Debug.Assert(tokens != null);
        Debug.Assert(tokens.AccessToken != null);
        Debug.Assert(tokens.RefreshToken != null);
        return tokens.RefreshToken;
    }

    public async Task<string> RegisterAsync(string url, string username, string password)
    {
        serverUrl = url;
        using HttpResponseMessage response = await client.PostAsJsonAsync($"{serverUrl}/auth/login",
            new User(Username: username, Password: password));

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Debug.Assert(tokens != null);
        Debug.Assert(tokens.AccessToken != null);
        Debug.Assert(tokens.RefreshToken != null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return tokens.RefreshToken;
    }

    public async Task UnregisterAsync(string refreshToken)
    {
        _ = await client.DeleteAsync($"{serverUrl}/auth/logout");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshToken);
        _ = await client.DeleteAsync($"{serverUrl}/auth/logout");
        client.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<SynchronizationData> PullSyncAsync(long since = 0)
    {
        using var response = await client.GetAsync($"{serverUrl}/api/sync/pull?since={since}");
        response.EnsureSuccessStatusCode();
        var entities = await response.Content.ReadFromJsonAsync<SynchronizationData>();
        Debug.Assert(entities != null);
        return entities;
    }

    public async Task PushSyncAsync(SynchronizationData syncData)
    {
        using var response = await client.PutAsJsonAsync($"{serverUrl}/api/sync/push", syncData);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Guid>> GetIncompleteSessionIdsAsync()
    {
        using var response = await client.GetAsync($"{serverUrl}/api/session/incomplete");
        response.EnsureSuccessStatusCode();
        var incompleteSessions = await response.Content.ReadFromJsonAsync<List<Guid>>();
        return incompleteSessions ?? [];
    }

    public async Task<byte[]?> GetSessionPsstAsync(Guid id)
    {
        using var response = await client.GetAsync($"{serverUrl}/api/session/{id}/psst");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        using var response = await client.PatchAsync($"{serverUrl}/api/session/{id}/psst",
            new ByteArrayContent(data));
        response.EnsureSuccessStatusCode();
    }
}
