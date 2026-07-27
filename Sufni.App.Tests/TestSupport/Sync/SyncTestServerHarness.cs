using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Extensibility.Sync;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Tests.TestSupport.Sync;

internal sealed class SyncTestServerHarness
{
    public SyncTestServerHarness()
    {
        SecureStorage.GetStringAsync("ServerUrl").Returns("https://sync.example.test");
        SecureStorage.GetStringAsync("RefreshToken").Returns("refresh-1");
        HttpApiService.ServerUrl.Returns("https://temporary-sync-endpoint.test");
        HttpApiService.GetIncompleteSessionIdsAsync().Returns([]);
        SessionRepository.GetIncompleteSessionIdsAsync().Returns([]);
        SessionRepository.GetIncompleteSessionIdsWithFingerprintAsync().Returns([]);
        HttpApiService.GetIncompleteSessionSourceIdsAsync().Returns([]);
        RecordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync().Returns([]);
        RecordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([]);
        AppPreferences.GetSyncDataAsync(Arg.Any<long>(), Arg.Any<long>()).Returns((AppPreferencesSyncData?)null);
        AppPreferences.ApplySyncDataAsync(Arg.Any<AppPreferencesSyncData?>()).Returns(Task.CompletedTask);
    }

    public ISecureStorage SecureStorage { get; } = Substitute.For<ISecureStorage>();
    public ISyncDataStore SyncDataStore { get; } = Substitute.For<ISyncDataStore>();
    public ISessionRepository SessionRepository { get; } = Substitute.For<ISessionRepository>();
    public ISessionStoreWriter SessionStore { get; } = Substitute.For<ISessionStoreWriter>();
    public IRecordedSessionSourceRepository RecordedSessionSourceRepository { get; } = Substitute.For<IRecordedSessionSourceRepository>();
    public IRecordedSessionSourceStoreWriter SourceStore { get; } = Substitute.For<IRecordedSessionSourceStoreWriter>();
    public IRecordedSessionSourceSyncQuery RecordedSessionSourceSyncQuery { get; } = Substitute.For<IRecordedSessionSourceSyncQuery>();
    public IHttpApiService HttpApiService { get; } = Substitute.For<IHttpApiService>();
    public IAppPreferences AppPreferences { get; } = Substitute.For<IAppPreferences>();

    public HttpApiService CreateHttpApiService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        new(SecureStorage, new HttpClient(new StubHttpMessageHandler(sendAsync)));

    public SynchronizationClientService CreateSynchronizationClientService(IExtensionSyncService? extensionSync = null) =>
        new(
            SyncDataStore,
            SessionRepository,
            SessionStore,
            RecordedSessionSourceRepository,
            SourceStore,
            RecordedSessionSourceSyncQuery,
            HttpApiService,
            AppPreferences,
            extensionSync);

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
        var data = payload ?? [8, 6, 7, 5];
        return new RecordedSessionSource
        {
            SessionId = Guid.NewGuid(),
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "sync.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "sync.SST",
                1,
                data),
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
        return JsonSerializer.Deserialize<T>(json);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }
}
