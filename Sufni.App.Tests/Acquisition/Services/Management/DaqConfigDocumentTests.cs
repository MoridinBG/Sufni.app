
using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Tests.Acquisition.Services.Management;

public class DaqConfigDocumentTests
{
    [Fact]
    public void Parse_UsesLastKnownAssignmentAsEffectiveValue()
    {
        var document = DaqConfigDocument.Parse("""
STA_SSID=first
SSID=second
STA_SSID=third
""");

        Assert.Equal("third", document.GetValue("STA_SSID"));
    }

    [Fact]
    public void BuildText_CanonicalizesAliases_CollapsesDuplicates_AndPreservesUnknownLines()
    {
        var document = DaqConfigDocument.Parse("""
# before
SSID=old-sta
UNKNOWN=value
PSK=old-psk
STA_SSID=duplicate-that-must-not-win
AP_SSID=old-ap
# trailing
""");
        var values = ToEditableValues(document);
        values["STA_SSID"] = "edited-sta";
        values["STA_PSK"] = "edited-psk";
        values["AP_SSID"] = "edited-ap";

        var saved = document.BuildText(values);
        var lines = saved.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("# before", lines);
        Assert.Contains("UNKNOWN=value", lines);
        Assert.Contains("# trailing", lines);
        Assert.Contains("STA_SSID=edited-sta", lines);
        Assert.Contains("STA_PSK=edited-psk", lines);
        Assert.Contains("AP_SSID=edited-ap", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("SSID=", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("PSK=", StringComparison.Ordinal));
        Assert.Equal(1, lines.Count(line => line.StartsWith("STA_SSID=", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("WIFI_MODE", "CLIENT")]
    [InlineData("COUNTRY", "H")]
    [InlineData("TRAVEL_SAMPLE_RATE", "0")]
    [InlineData("IMU_SAMPLE_RATE", "65536")]
    [InlineData("GPS_SAMPLE_RATE", "abc")]
    [InlineData("STA_SSID", "sssssssssssssssssssssssssssssssss")]
    [InlineData("AP_PSK", "abc=defgh")]
    public void Validate_RejectsInvalidFieldWithoutRejectingValidConfiguration(string key, string invalid)
    {
        var values = ToEditableValues(DaqConfigDocument.Parse("WIFI_MODE=AP\nAP_SSID=trail\nAP_PSK=trail-secret"));
        Assert.Empty(DaqConfigValidator.Validate(values));
        values[key] = invalid;
        Assert.Equal(key, Assert.Single(DaqConfigValidator.Validate(values)).Key);
    }

    [Fact]
    public void Validate_RejectsMissingCredentialsForActiveWifiMode()
    {
        var staValues = ToEditableValues(DaqConfigDocument.Parse(string.Empty));
        staValues["WIFI_MODE"] = "STA";
        staValues["STA_SSID"] = string.Empty;
        staValues["STA_PSK"] = string.Empty;

        var staErrors = DaqConfigValidator.Validate(staValues);

        Assert.Contains("STA_SSID", staErrors.Keys);
        Assert.Contains("STA_PSK", staErrors.Keys);

        var apValues = ToEditableValues(DaqConfigDocument.Parse(string.Empty));
        apValues["WIFI_MODE"] = "AP";
        apValues["AP_SSID"] = string.Empty;
        apValues["AP_PSK"] = "short";

        var apErrors = DaqConfigValidator.Validate(apValues);

        Assert.Contains("AP_SSID", apErrors.Keys);
        Assert.Contains("AP_PSK", apErrors.Keys);
    }

    private static Dictionary<string, string> ToEditableValues(DaqConfigDocument document) =>
        document.GetFieldValues().ToDictionary(value => value.Definition.Key, value => value.Value);
}