using System.Text.Json;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.Infrastructure;

// SUT: AppJson's hardened inbound path (InboundOptions / InboundContext, §5.1).
// The desktop sync server's JSON endpoints deserialize their bodies through
// AppJson.InboundContext, so these tests lock the trust-boundary contract: a
// malformed or under-specified inbound payload is rejected there.
// The 429 / 401 wire behaviors and the [FromBody] Http.Json options live in the
// desktop Kestrel pipeline and are not exercised here (no live server is booted).
public class AppJsonInboundHardeningTests
{
    [Theory]
    [InlineData("""{"device_id":"device-a","device_id":"device-b","display_name":null,"pin":"123456"}""")]
    [InlineData("""{"Device_Id":"device-a","display_name":null,"pin":"123456"}""")]
    [InlineData("""{"device_id":"device-a","display_name":null,"pin":"123456","unexpected":true}""")]
    public void InboundContext_RejectsPairingConfirm_WithMalformedJsonContract(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AppJson.InboundContext.PairingConfirm));
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
}
