namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveDaqSharedStreamState(
    LiveConnectionState ConnectionState,
    string? LastError,
    LiveSessionHeader? SessionHeader,
    LiveStreamMask SelectedStreamMask,
    bool IsConfigurationLocked,
    bool IsClosed,
    LiveProtocolVersion ProtocolVersion = LiveProtocolVersion.V2)
{
    public LiveDaqClientDropCounters ClientDropCounters { get; init; } = LiveDaqClientDropCounters.Empty;

    public static readonly LiveDaqSharedStreamState Empty = new(
        ConnectionState: LiveConnectionState.Disconnected,
        LastError: null,
        SessionHeader: null,
        SelectedStreamMask: LiveStreamMask.None,
        IsConfigurationLocked: false,
        IsClosed: false,
        ProtocolVersion: LiveProtocolVersion.V2);
}
