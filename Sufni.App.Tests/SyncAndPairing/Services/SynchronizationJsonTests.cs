using System.Text.Json;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Sync;

namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizationJsonTests
{
    [Theory]
    [InlineData("""{"device_id":"device-a","device_id":"device-b","display_name":null,"pin":"123456"}""")]
    [InlineData("""{"Device_Id":"device-a","display_name":null,"pin":"123456"}""")]
    [InlineData("""{"device_id":"device-a","display_name":null,"pin":"123456","unexpected":true}""")]
    public void ServerJson_RejectsPairingConfirm_WithMalformedJsonContract(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PairingConfirm>(json, SyncTestServerHarness.CreateServerJsonOptions()));
    }

    [Fact]
    public void ServerJson_RejectsPairingConfirm_MissingPin()
    {
        // The security-critical case: a pairing confirmation that omits the PIN must be
        // rejected at bind time rather than constructing PairingConfirm with a null Pin.
        const string json = """{"device_id":"device-a","display_name":"Phone"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PairingConfirm>(json, SyncTestServerHarness.CreateServerJsonOptions()));
    }

    [Fact]
    public void ServerJson_AcceptsPairingConfirm_WithExplicitNullDisplayName()
    {
        // Real client shape: display_name is string? and is emitted as explicit null
        // when unset. An explicit null binds; pin (non-nullable) stays required.
        const string json = """{"device_id":"device-a","display_name":null,"pin":"123456"}""";

        var confirm = JsonSerializer.Deserialize<PairingConfirm>(json, SyncTestServerHarness.CreateServerJsonOptions());

        Assert.NotNull(confirm);
        Assert.Equal("device-a", confirm!.DeviceId);
        Assert.Equal("123456", confirm.Pin);
        Assert.Null(confirm.DisplayName);
    }
}
