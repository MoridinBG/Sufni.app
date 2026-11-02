using System;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Models;

namespace Sufni.App.LiveDaq.ViewModels;

internal static class LiveDaqMessages
{
    public static FailurePresentation? ConnectionFailure(LiveConnectionState state) => state switch
    {
        LiveConnectionState.Disconnected { Outcome: LivePreviewStartResult.Failed failed } => new(failed.Message, failed.Details),
        LiveConnectionState.Disconnected { Outcome: LivePreviewStartResult.Rejected rejected } => new(rejected.Message),
        LiveConnectionState.Closed { Reason: LiveStreamClosedReason.TransportFailure or LiveStreamClosedReason.ConnectionLost } closed => new(closed.Message, closed.Details),
        _ => null
    };

    public static string Busy(DaqTransportHolder holder) => holder switch
    {
        DaqTransportHolder.Streaming => "DAQ is busy: streaming.",
        DaqTransportHolder.Import => "DAQ is busy: importing.",
        DaqTransportHolder.Identify => "DAQ is busy: identifying the device.",
        DaqTransportHolder.Management => "DAQ is busy: device management.",
        _ => throw new ArgumentOutOfRangeException(nameof(holder))
    };

}
