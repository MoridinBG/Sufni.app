using System;

namespace Sufni.App.Services.Management;

public abstract record DaqSetTimeResult
{
    private DaqSetTimeResult()
    {
    }

    public sealed record Ok(TimeSpan RoundTripTime) : DaqSetTimeResult;

    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqSetTimeResult;
}