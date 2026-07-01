using System.Text.Json;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.Tests.Infrastructure;

// SUT: AppJson's hardened inbound path (InboundOptions / InboundContext, §5.1).
// The desktop sync server's two PATCH endpoints deserialize their bodies through
// AppJson.InboundContext, so these tests lock the trust-boundary contract: a
// malformed or under-specified inbound payload is rejected there, while the lenient
// AppJson.Context (client + local round-trips) deliberately stays permissive.
// The 429 / 401 wire behaviors and the [FromBody] Http.Json options live in the
// desktop Kestrel pipeline and are not exercised here (no live server is booted).
public class AppJsonInboundHardeningTests
{
    [Fact]
    public void InboundContext_DeserializesValidSessionDataTransfer()
    {
        // byte[] is JSON-encoded as base64; "AQID" == { 1, 2, 3 }.
        const string json = """{"processing_fingerprint":"fp-1","data":"AQID"}""";

        var transfer = JsonSerializer.Deserialize(json, AppJson.InboundContext.SessionDataTransfer);

        Assert.NotNull(transfer);
        Assert.Equal("fp-1", transfer!.Fingerprint);
        Assert.Equal(new byte[] { 1, 2, 3 }, transfer.Data);
    }

    [Fact]
    public void InboundContext_AcceptsExplicitNullFingerprint()
    {
        // The client always emits every key (no ignore-null condition), writing an
        // explicit null for a legacy blob's absent fingerprint. processing_fingerprint
        // is string?, so an explicit null binds and the legacy fill path keeps working.
        const string json = """{"processing_fingerprint":null,"data":"AQID"}""";

        var transfer = JsonSerializer.Deserialize(json, AppJson.InboundContext.SessionDataTransfer);

        Assert.NotNull(transfer);
        Assert.Null(transfer!.Fingerprint);
        Assert.Equal(new byte[] { 1, 2, 3 }, transfer.Data);
    }

    [Fact]
    public void InboundContext_RequiresNullableKeyPresent_EvenWhenNullable()
    {
        // Sharp edge of RespectRequiredConstructorParameters: a positional record
        // parameter with no default is required-as-present even when its type is
        // nullable. Omitting processing_fingerprint (rather than sending null) is
        // therefore rejected, which is why the client must keep emitting null keys.
        const string json = """{"data":"AQID"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SessionDataTransfer));
    }

    [Fact]
    public void InboundContext_RejectsMissingRequiredMember()
    {
        // data is a non-nullable constructor parameter, so it must be present.
        const string json = """{"processing_fingerprint":"fp-1"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SessionDataTransfer));
    }

    [Fact]
    public void InboundContext_RejectsUnmappedMember()
    {
        const string json = """{"processing_fingerprint":"fp-1","data":"AQID","unexpected":true}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SessionDataTransfer));
    }

    [Fact]
    public void InboundContext_RejectsDuplicateProperty()
    {
        const string json = """{"data":"AQID","processing_fingerprint":"fp-1","data":"BAUG"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SessionDataTransfer));
    }

    [Fact]
    public void InboundContext_IsCaseSensitive()
    {
        // Wrong-cased names do not bind; the required data member then goes missing.
        const string json = """{"Processing_Fingerprint":"fp-1","Data":"AQID"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.SessionDataTransfer));
    }

    [Fact]
    public void InboundContext_DeserializesSnakeCaseEnum_OnRecordedSourceTransfer()
    {
        const string json =
            """{"session_id":"11111111-1111-1111-1111-111111111111","source_kind":"imported_sst","source_name":"ride.sst","schema_version":4,"source_hash":"h","payload":"AQID"}""";

        var transfer = JsonSerializer.Deserialize(json, AppJson.InboundContext.RecordedSessionSourceTransfer);

        Assert.NotNull(transfer);
        Assert.Equal(RecordedSessionSourceKind.ImportedSst, transfer!.SourceKind);
        Assert.Equal("ride.sst", transfer.SourceName);
    }

    [Fact]
    public void InboundContext_RejectsRecordedSourceTransfer_MissingRequiredMember()
    {
        // source_name omitted — a non-nullable constructor parameter.
        const string json =
            """{"session_id":"11111111-1111-1111-1111-111111111111","source_kind":"imported_sst","schema_version":4,"source_hash":"h","payload":"AQID"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.RecordedSessionSourceTransfer));
    }

    [Fact]
    public void InboundContext_RejectsPairingConfirm_MissingPin()
    {
        // The security-critical case: a pairing confirmation that omits the PIN must be
        // rejected at bind time rather than constructing PairingConfirm with a null Pin.
        const string json = """{"device_id":"device-a","display_name":"Phone"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.PairingConfirm));
    }

    [Fact]
    public void InboundContext_AcceptsPairingConfirm_WithExplicitNullDisplayName()
    {
        // Real client shape: display_name is string? and is emitted as explicit null
        // when unset. An explicit null binds; pin (non-nullable) stays required.
        const string json = """{"device_id":"device-a","display_name":null,"pin":"123456"}""";

        var confirm = JsonSerializer.Deserialize(json, AppJson.InboundContext.PairingConfirm);

        Assert.NotNull(confirm);
        Assert.Equal("device-a", confirm!.DeviceId);
        Assert.Equal("123456", confirm.Pin);
        Assert.Null(confirm.DisplayName);
    }

    [Fact]
    public void LenientContext_StaysPermissive_WhereInboundRejects()
    {
        // The split is real: the same unmapped-member body the inbound boundary rejects
        // round-trips through the lenient context the client and local storage use.
        const string json = """{"processing_fingerprint":"fp-1","data":"AQID","unexpected":true}""";

        var transfer = JsonSerializer.Deserialize(json, AppJson.Context.SessionDataTransfer);

        Assert.NotNull(transfer);
        Assert.Equal("fp-1", transfer!.Fingerprint);
    }
}
