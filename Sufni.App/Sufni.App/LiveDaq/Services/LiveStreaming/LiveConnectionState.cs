namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public enum LiveStreamClosedReason { Offline, ProtocolChanged, ConnectionLost, TransportFailure, Disposed }

public abstract record LiveConnectionState
{
    private LiveConnectionState() { }
    public virtual string? Message => null;
    public sealed record Disconnected(LivePreviewStartResult.NotStarted? Outcome = null) : LiveConnectionState
    {
        public override string? Message => Outcome?.Message;
    }
    public sealed record Connecting : LiveConnectionState;
    public sealed record Connected : LiveConnectionState;
    public sealed record Disconnecting : LiveConnectionState;
    public sealed record Closed(LiveStreamClosedReason Reason, string? Details = null) : LiveConnectionState
    {
        public override string Message => Reason switch
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
