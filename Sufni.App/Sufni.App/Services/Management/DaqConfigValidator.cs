using System.Collections.Generic;

namespace Sufni.App.Services.Management;

public static class DaqConfigValidator
{
    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyDictionary<string, string> values)
    {
        var errors = new Dictionary<string, string>();

        foreach (var field in DaqConfigFields.All)
        {
            if (field.MaxLength is not { } maxLength)
            {
                continue;
            }

            var value = GetValue(values, field);
            if (value.Length > maxLength)
            {
                errors[field.Key] = $"Must be {maxLength} characters or fewer.";
            }
            else if (value.Contains('='))
            {
                errors[field.Key] = "Must not contain '='.";
            }
        }

        var wifiMode = GetValue(values, DaqConfigFields.WifiMode);
        if (wifiMode != "STA" && wifiMode != "AP")
        {
            errors[DaqConfigFields.WifiMode.Key] = "Must be STA or AP.";
        }

        if (GetValue(values, DaqConfigFields.Country).Length < 2)
        {
            errors[DaqConfigFields.Country.Key] = "Must be at least 2 characters.";
        }

        ValidateSampleRate(values, errors, DaqConfigFields.TravelSampleRate);
        ValidateSampleRate(values, errors, DaqConfigFields.ImuSampleRate);
        ValidateSampleRate(values, errors, DaqConfigFields.GpsSampleRate);

        if (wifiMode == "STA")
        {
            if (GetValue(values, DaqConfigFields.StaSsid).Length == 0)
            {
                errors[DaqConfigFields.StaSsid.Key] = "Required when WiFi mode is STA.";
            }

            if (GetValue(values, DaqConfigFields.StaPsk).Length == 0)
            {
                errors[DaqConfigFields.StaPsk.Key] = "Required when WiFi mode is STA.";
            }
        }
        else if (wifiMode == "AP")
        {
            if (GetValue(values, DaqConfigFields.ApSsid).Length == 0)
            {
                errors[DaqConfigFields.ApSsid.Key] = "Required when WiFi mode is AP.";
            }

            if (GetValue(values, DaqConfigFields.ApPsk).Length < 8)
            {
                errors[DaqConfigFields.ApPsk.Key] = "Must be at least 8 characters when WiFi mode is AP.";
            }
        }

        return errors;
    }

    private static void ValidateSampleRate(
        IReadOnlyDictionary<string, string> values,
        Dictionary<string, string> errors,
        DaqConfigFieldDefinition field)
    {
        if (!ushort.TryParse(GetValue(values, field), out var rate) || rate < 1)
        {
            errors[field.Key] = "Must be a number from 1 to 65535.";
        }
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, DaqConfigFieldDefinition field) =>
        values.TryGetValue(field.Key, out var value) ? value : field.DefaultValue;
}
