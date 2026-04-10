using System;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.Models;

public record PairingRequest(
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("display_name")] string? DisplayName);

public record PairingConfirm(
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("pin")] string Pin);

public record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

public record RefreshRequest(
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

public record UnpairRequest(
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

[Table("paired_device")]
public class PairedDevice
{
    [Column("device_id")]
    [PrimaryKey]
    public string DeviceId { get; set; } = null!;

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("token")]
    public string Token { get; set; } = null!;

    [Column("expires")]
    public DateTime Expires { get; set; }

    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public PairedDevice() { }

    public PairedDevice(string deviceId, string? displayName, DateTime expires)
    {
        DeviceId = deviceId;
        DisplayName = NormalizeDisplayName(displayName);
        Expires = expires;
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    /// <summary>
    /// Canonicalizes a display name: returns <c>null</c> if the input
    /// is <c>null</c>, empty, or only whitespace; otherwise returns
    /// the trimmed value. Applied at every boundary that stores or
    /// transmits a display name so that the desktop's
    /// <c>DisplayName ?? DeviceId</c> fallback never has to consider
    /// blank strings as a "real" name.
    /// </summary>
    public static string? NormalizeDisplayName(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}