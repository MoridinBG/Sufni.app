namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public enum LiveStreamClosedReason { Offline, ProtocolChanged, ConnectionLost, TransportFailure, Disposed }

public abstract record LiveConnectionState
{
    private LiveConnectionState() { }
    public sealed record Disconnected(LivePreviewStartResult.NotStarted? Outcome = null) : LiveConnectionState;
    public sealed record Connecting : LiveConnectionState;
    public sealed record Connected : LiveConnectionState;
    public sealed record Disconnecting : LiveConnectionState;
    public sealed record Closed(LiveStreamClosedReason Reason, string? Details = null) : LiveConnectionState
    {
        public string Message => Reason switch
        {
            LiveStreamClosedReason.Offline => "DAQ went offline.",
            LiveStreamClosedReason.ProtocolChanged => "DAQ protocol changed. Reopen the live tab.",
            LiveStreamClosedReason.ConnectionLost => "The DAQ connection ended. Reopen the live tab to reconnect.",
            LiveStreamClosedReason.TransportFailure => "The DAQ connection failed. Reopen the live tab to retry.",
            LiveStreamClosedReason.Disposed => "The live connection is closed.",
            _ => throw new System.ArgumentOutOfRangeException(nameof(Reason))
        };
    }
}
