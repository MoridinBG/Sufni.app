namespace Sufni.App.Services.LiveStreaming;

public sealed record LiveDaqSharedStreamState(
    LiveConnectionState ConnectionState,
    string? LastError,
    LiveSessionHeader? SessionHeader,
    LiveSensorMask SelectedSensorMask,
    bool IsConfigurationLocked,
    bool IsClosed)
{
    public LiveDaqClientDropCounters ClientDropCounters { get; init; } = LiveDaqClientDropCounters.Empty;

    public static readonly LiveDaqSharedStreamState Empty = new(
        ConnectionState: LiveConnectionState.Disconnected,
        LastError: null,
        SessionHeader: null,
        SelectedSensorMask: LiveSensorMask.None,
        IsConfigurationLocked: false,
        IsClosed: false);
}