using System;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public enum DaqManagementOperation { SetTime, ReadConfig, WriteConfig, ListFiles }

// The operation holding the DAQ's single-client port, independently of tab observers.
public abstract record DaqTransportHolder
{
    private DaqTransportHolder() { }
    public sealed record Streaming : DaqTransportHolder;
    public sealed record Identify : DaqTransportHolder;
    public sealed record Management(DaqManagementOperation Operation) : DaqTransportHolder;
    public sealed record Import(Guid BatchId) : DaqTransportHolder;
}

public abstract record DaqTransportAcquisition
{
    private DaqTransportAcquisition() { }
    public sealed record Acquired(IDisposable Lease) : DaqTransportAcquisition;
    public sealed record Busy(DaqTransportHolder Holder) : DaqTransportAcquisition;
}
