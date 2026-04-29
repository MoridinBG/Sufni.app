using System;
using System.Collections.Generic;

namespace Sufni.App.Services.Management;

public static class DaqConfigValidator
{
    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyDictionary<string, string> values)
    {
        var errors = new Dictionary<string, string>();

        foreach (var field in DaqConfigFields.All)
        {
            var value = GetValue(values, field);
            if (field.MaxLength is { } maxLength && value.Length > maxLength)
            {
                errors[field.Key] = $"Must be {maxLength} characters or fewer.";
            }

            if (field.MaxLength is not null && value.Contains('=', StringComparison.Ordinal))
            {
                errors[field.Key] = "Must not contain '='.";
            }
        }

        var wifiMode = GetValue(values, DaqConfigFields.WifiMode);
        if (!string.Equals(wifiMode, "STA", StringComparison.Ordinal)
            && !string.Equals(wifiMode, "AP", StringComparison.Ordinal))
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

        if (string.Equals(wifiMode, "STA", StringComparison.Ordinal))
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
        else if (string.Equals(wifiMode, "AP", StringComparison.Ordinal))
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
        if (!TryParseFirmwareUnsigned16(GetValue(values, field), out _))
        {
            errors[field.Key] = "Must be a number from 1 to 65535.";
        }
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, DaqConfigFieldDefinition field) =>
        values.TryGetValue(field.Key, out var value) ? value : field.DefaultValue;

    private static bool TryParseFirmwareUnsigned16(string value, out ushort parsed)
    {
        parsed = 0;
        var index = 0;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        if (index == value.Length || !char.IsDigit(value[index]))
        {
            return false;
        }

        ulong accumulated = 0;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            accumulated = (accumulated * 10) + (uint)(value[index] - '0');
            if (accumulated > ushort.MaxValue)
            {
                return false;
            }

            index++;
        }

        if (accumulated == 0)
        {
            return false;
        }

        parsed = (ushort)accumulated;
        return true;
    }
}