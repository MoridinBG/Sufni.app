namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveDaqSharedStreamState(
    LiveConnectionState ConnectionState,
    LiveSessionHeader? SessionHeader,
    LiveStreamMask SelectedStreamMask,
    bool IsConfigurationLocked,
    LiveProtocolVersion ProtocolVersion = LiveProtocolVersion.V2)
{
    public bool IsClosed => ConnectionState is LiveConnectionState.Closed;
    public string? LastError => ConnectionState switch
    {
        LiveConnectionState.Closed closed => closed.Message,
        LiveConnectionState.Disconnected disconnected => disconnected.Outcome?.Message,
        _ => null
    };

    public LiveDaqClientDropCounters ClientDropCounters { get; init; } = LiveDaqClientDropCounters.Empty;

    public static readonly LiveDaqSharedStreamState Empty = new(
        ConnectionState: new LiveConnectionState.Disconnected(),
        SessionHeader: null,
        SelectedStreamMask: LiveStreamMask.None,
        IsConfigurationLocked: false,
        ProtocolVersion: LiveProtocolVersion.V2);
}
