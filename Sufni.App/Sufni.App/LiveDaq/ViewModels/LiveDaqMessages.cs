using Sufni.App.LiveDaq.Coordinators;
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

    public static FailurePresentation? ManagementFailure(LiveDaqManagementResult result) => result switch
    {
        LiveDaqManagementResult.Failed failed => FailurePresentation.From(failed.Reason, failed.Details),
        LiveDaqManagementResult.Rejected rejected => new("The DAQ rejected the operation. Check its configuration and try again.", rejected.Details),
        LiveDaqManagementResult.Busy busy => new(Busy(busy.Holder)),
        LiveDaqManagementResult.Unavailable unavailable => new(Unavailable(unavailable.Reason)),
        _ => null
    };

    public static string Unavailable(DaqManagementUnavailability reason) => reason switch
    {
        DaqManagementUnavailability.Offline => "DAQ is offline.",
        DaqManagementUnavailability.Unsupported => "Management is not supported by this DAQ.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };
}
