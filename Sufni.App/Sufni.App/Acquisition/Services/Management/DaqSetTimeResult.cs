using System;

namespace Sufni.App.Acquisition.Services.Management;

public closed record DaqSetTimeResult
{
    private DaqSetTimeResult()
    {
    }

    public sealed record Ok(TimeSpan RoundTripTime) : DaqSetTimeResult;

    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqSetTimeResult;
}