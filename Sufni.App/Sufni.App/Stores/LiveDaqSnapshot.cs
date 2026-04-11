namespace Sufni.App.Stores;

/// <summary>
/// Runtime-only view of a live DAQ entry as currently known to the app.
/// This is not backed by persisted-store conventions and is keyed by the
/// identity used to route list selection and future detail tabs.
/// </summary>
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