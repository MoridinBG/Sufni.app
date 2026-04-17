namespace Sufni.App.Services.Management;

public abstract record DaqManagementResult
{
    private DaqManagementResult()
    {
    }

    public sealed record Ok : DaqManagementResult;

    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqManagementResult;
}