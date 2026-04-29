using Sufni.App.Services.Management;

namespace Sufni.App.Tests.Services.Management;

public class DaqConfigDocumentTests
{
    [Fact]
    public void Parse_ReturnsKnownFieldsInFirmwareOrder_WithAliasesAndDefaults()
    {
        var document = DaqConfigDocument.Parse("""
WIFI_MODE=AP
SSID=legacy-sta
PSK=legacy-psk
AP_SSID=trackside
AP_PSK=trackside-secret
TRAVEL_SAMPLE_RATE=400
IMU_SAMPLE_RATE=100
GPS_SAMPLE_RATE=5
""");

        var values = document.GetFieldValues();

        Assert.Equal(DaqConfigFields.All.Select(field => field.Key), values.Select(value => value.Definition.Key));
        Assert.Equal("AP", document.GetValue("WIFI_MODE"));
        Assert.Equal("legacy-sta", document.GetValue("STA_SSID"));
        Assert.Equal("legacy-psk", document.GetValue("STA_PSK"));
        Assert.Equal("trackside", document.GetValue("AP_SSID"));
        Assert.Equal("trackside-secret", document.GetValue("AP_PSK"));
        Assert.Equal("pool.ntp.org", document.GetValue("NTP_SERVER"));
        Assert.Equal("HU", document.GetValue("COUNTRY"));
        Assert.Equal("UTC0", document.GetValue("TIMEZONE"));
        Assert.Equal("400", document.GetValue("TRAVEL_SAMPLE_RATE"));
        Assert.Equal("100", document.GetValue("IMU_SAMPLE_RATE"));
        Assert.Equal("5", document.GetValue("GPS_SAMPLE_RATE"));
    }

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

    [Fact]
    public void Validate_AcceptsDefaultFieldValues()
    {
        var document = DaqConfigDocument.Parse(string.Empty);

        var errors = DaqConfigValidator.Validate(ToEditableValues(document));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsInvalidEnumsCountryAndSampleRates()
    {
        var values = ToEditableValues(DaqConfigDocument.Parse(string.Empty));
        values["WIFI_MODE"] = "CLIENT";
        values["COUNTRY"] = "H";
        values["TRAVEL_SAMPLE_RATE"] = "0";
        values["IMU_SAMPLE_RATE"] = "65536";
        values["GPS_SAMPLE_RATE"] = "abc";

        var errors = DaqConfigValidator.Validate(values);

        Assert.Contains("WIFI_MODE", errors.Keys);
        Assert.Contains("COUNTRY", errors.Keys);
        Assert.Contains("TRAVEL_SAMPLE_RATE", errors.Keys);
        Assert.Contains("IMU_SAMPLE_RATE", errors.Keys);
        Assert.Contains("GPS_SAMPLE_RATE", errors.Keys);
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

    [Fact]
    public void Validate_RejectsStringValuesThatFirmwareWouldTruncateOrSplit()
    {
        var values = ToEditableValues(DaqConfigDocument.Parse(string.Empty));
        values["STA_SSID"] = new string('s', 33);
        values["AP_PSK"] = "abc=defgh";
        values["NTP_SERVER"] = new string('n', 264);

        var errors = DaqConfigValidator.Validate(values);

        Assert.Contains("STA_SSID", errors.Keys);
        Assert.Contains("AP_PSK", errors.Keys);
        Assert.Contains("NTP_SERVER", errors.Keys);
    }

    private static Dictionary<string, string> ToEditableValues(DaqConfigDocument document) =>
        document.GetFieldValues().ToDictionary(value => value.Definition.Key, value => value.Value);
}