using System;
using System.Collections.Generic;
using System.Linq;

namespace Sufni.App.Services.Management;

public sealed class DaqConfigFieldDefinition(
    string key,
    string label,
    string defaultValue,
    int? maxLength = null,
    bool isSecret = false,
    IReadOnlyList<string>? aliases = null)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string DefaultValue { get; } = defaultValue;
    public int? MaxLength { get; } = maxLength;
    public bool IsSecret { get; } = isSecret;
    public IReadOnlyList<string> Aliases { get; } = aliases ?? [];

    public bool Matches(string candidate) =>
        Key == candidate || Aliases.Contains(candidate);
}

public sealed record DaqConfigFieldValue(DaqConfigFieldDefinition Definition, string Value);

public static class DaqConfigFields
{
    public static DaqConfigFieldDefinition WifiMode { get; } = new("WIFI_MODE", "WiFi mode", "STA");

    public static DaqConfigFieldDefinition StaSsid { get; } = new("STA_SSID", "Station SSID", "sst", maxLength: 32, aliases: ["SSID"]);

    public static DaqConfigFieldDefinition StaPsk { get; } = new("STA_PSK", "Station PSK", "changemeplease", maxLength: 63, isSecret: true, aliases: ["PSK"]);

    public static DaqConfigFieldDefinition ApSsid { get; } = new("AP_SSID", "Access point SSID", "SufniDAQ", maxLength: 32);

    public static DaqConfigFieldDefinition ApPsk { get; } = new("AP_PSK", "Access point PSK", "changemeplease", maxLength: 63, isSecret: true);

    public static DaqConfigFieldDefinition NtpServer { get; } = new("NTP_SERVER", "NTP server", "pool.ntp.org", maxLength: 263);

    public static DaqConfigFieldDefinition Country { get; } = new("COUNTRY", "Country", "HU");

    public static DaqConfigFieldDefinition Timezone { get; } = new("TIMEZONE", "Timezone", "UTC0", maxLength: 99);

    public static DaqConfigFieldDefinition TravelSampleRate { get; } = new("TRAVEL_SAMPLE_RATE", "Travel sample rate", "200");

    public static DaqConfigFieldDefinition ImuSampleRate { get; } = new("IMU_SAMPLE_RATE", "IMU sample rate", "200");

    public static DaqConfigFieldDefinition GpsSampleRate { get; } = new("GPS_SAMPLE_RATE", "GPS sample rate", "1");

    public static IReadOnlyList<DaqConfigFieldDefinition> All { get; } =
    [
        WifiMode,
        StaSsid,
        StaPsk,
        ApSsid,
        ApPsk,
        NtpServer,
        Country,
        Timezone,
        TravelSampleRate,
        ImuSampleRate,
        GpsSampleRate
    ];

    public static DaqConfigFieldDefinition Get(string key) =>
        TryGet(key, out var definition)
            ? definition
            : throw new ArgumentException($"Unknown CONFIG field '{key}'.", nameof(key));

    public static bool TryGet(string key, out DaqConfigFieldDefinition definition)
    {
        foreach (var field in All)
        {
            if (field.Matches(key))
            {
                definition = field;
                return true;
            }
        }

        definition = null!;
        return false;
    }
}