namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveDaqSharedStreamState(
    LiveConnectionState ConnectionState,
    string? Error,
    LiveSessionHeader? SessionHeader,
    LiveStreamMask SelectedStreamMask,
    bool IsConfigurationLocked,
    LiveProtocolVersion ProtocolVersion = LiveProtocolVersion.V2)
{
    public bool IsClosed => ConnectionState is LiveConnectionState.Closed;
    public string? LastError => ConnectionState is LiveConnectionState.Closed closed ? closed.Message : Error;

    public LiveDaqClientDropCounters ClientDropCounters { get; init; } = LiveDaqClientDropCounters.Empty;

    public static readonly LiveDaqSharedStreamState Empty = new(
        ConnectionState: new LiveConnectionState.Disconnected(),
        Error: null,
        SessionHeader: null,
        SelectedStreamMask: LiveStreamMask.None,
        IsConfigurationLocked: false,
        ProtocolVersion: LiveProtocolVersion.V2);
}
