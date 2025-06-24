using System;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.Models;

public record User(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

public record PairingRequest(
    [property: JsonPropertyName("device_id")]string DeviceId);

public record PairingConfirm(
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("pin")] string Pin);

public record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

public record RefreshRequest(
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

[Table("refresh_token")]
public class RefreshToken
{
    [Column("device_id")]
    [PrimaryKey]
    public string DeviceId { get; set; } = null!;
    
    [Column("token")]
    public string Token { get; set; } = null!;

    [Column("expires")]
    public DateTime Expires { get; set; }
    
    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public RefreshToken() { }

    public RefreshToken(string deviceId, DateTime expires)
    {
        DeviceId = deviceId;
        Expires = expires;
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}