using System;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Models;

namespace Sufni.App.LiveDaq.Coordinators;

public sealed record LiveDaqManagementAvailability(LiveDaqCatalogEntry? Device, DaqTransportHolder? Holder)
{
    public bool CanManage => Device is { SupportsManagement: true } && Holder is null;
    public bool CanConnect => Device is not null && Holder is null;
}

public enum DaqManagementUnavailability { Offline, Unsupported }

public abstract record LiveDaqManagementResult
{
    private LiveDaqManagementResult() { }
    public sealed record TimeSet(TimeSpan RoundTripTime) : LiveDaqManagementResult;
    public sealed record ConfigRead(DaqConfigDocument Document) : LiveDaqManagementResult;
    public sealed record ConfigWritten : LiveDaqManagementResult;
    public sealed record ConfigSelected(SelectedDeviceConfigFile File) : LiveDaqManagementResult;
    public sealed record Canceled : LiveDaqManagementResult;
    public sealed record Busy(DaqTransportHolder Holder) : LiveDaqManagementResult;
    public sealed record Unavailable(DaqManagementUnavailability Reason) : LiveDaqManagementResult;
    public sealed record Rejected(DaqManagementErrorCode ErrorCode, string Message) : LiveDaqManagementResult;
    public sealed record Failed(DaqManagementFailureReason Reason, string? Details = null) : LiveDaqManagementResult;
}
