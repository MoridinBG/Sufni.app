using System;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.LiveDaq.ViewModels;

internal static class LiveDaqMessages
{
    public static string Busy(DaqTransportHolder holder) => holder switch
    {
        DaqTransportHolder.Streaming => "DAQ is busy: streaming.",
        DaqTransportHolder.Import => "DAQ is busy: importing.",
        DaqTransportHolder.Identify => "DAQ is busy: identifying the device.",
        DaqTransportHolder.Management => "DAQ is busy: device management.",
        _ => throw new ArgumentOutOfRangeException(nameof(holder))
    };

}
