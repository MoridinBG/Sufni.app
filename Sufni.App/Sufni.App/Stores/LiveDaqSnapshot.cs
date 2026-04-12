namespace Sufni.App.Stores;

// Runtime-only view of a live DAQ entry as currently known to the app.
public sealed record LiveDaqSnapshot(
    string IdentityKey,
    string DisplayName,
    string? BoardId,
    string? Host,
    int? Port,
    bool IsOnline,
    string? SetupName,
    string? BikeName)
{
    public string? Endpoint => Host is not null && Port is not null ? $"{Host}:{Port}" : null;
}