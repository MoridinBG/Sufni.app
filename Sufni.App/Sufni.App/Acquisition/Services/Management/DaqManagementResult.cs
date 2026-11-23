namespace Sufni.App.Acquisition.Services.Management;

public closed record DaqManagementResult
{
    private DaqManagementResult()
    {
    }

    public sealed record Ok : DaqManagementResult;

    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqManagementResult;
}