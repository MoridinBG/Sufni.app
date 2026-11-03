using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.App.SyncAndPairing.Services;

public static class SynchronizationProtocol
{
    public const string ServiceType = "_sstsync._tcp";
    public const string CertificateSubjectName = "cn=com.sghctoma.sst-api";
    public const int PinTtlSeconds = 30;
    public const int SyncProtocolVersion = 6;
    public const string SyncProtocolHeader = "X-Sufni-Sync-Protocol";
    public const string FingerprintHeader = "X-Sufni-Processing-Fingerprint";
    public const string SourceKindHeader = "X-Sufni-Source-Kind";
    public const string SourceNameHeader = "X-Sufni-Source-Name";
    public const string SchemaVersionHeader = "X-Sufni-Schema-Version";
    public const string SourceHashHeader = "X-Sufni-Source-Hash";
    public const string OctetStreamContentType = "application/octet-stream";
    public const string EndpointPairRequest = "/pair/request";
    public const string EndpointPairConfirm = "/pair/confirm";
    public const string EndpointPairRefresh = "/pair/refresh";
    public const string EndpointPairUnpair = "/pair/unpair";
    public const string RunIdHeader = "X-Sufni-Sync-Run";
    public const string RunPhaseHeader = "X-Sufni-Sync-Phase";
    public const string EndpointRunStart = "/sync/run/start";
    public const string EndpointRunEnd = "/sync/run/end";
    public const string EndpointSyncPush = "/sync/push";
    public const string EndpointSyncPull = "/sync/pull";
    public const string EndpointSessionIncomplete = "/session/incomplete";
    public const string EndpointSessionData = "/session/data/";
    public const string EndpointSessionSourceIncomplete = "/session/source/incomplete";
    public const string EndpointSessionSourceData = "/session/source/data/";
    /// <summary>Applies the server's request contract without changing its response naming policy.</summary>
    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.AllowDuplicateProperties = false;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.PropertyNameCaseInsensitive = false;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }
}
