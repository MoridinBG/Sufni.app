namespace Sufni.App.Acquisition.Services.Management;

public abstract record DaqConfigReadResult
{
    private DaqConfigReadResult() { }
    public sealed record Read(DaqConfigDocument Document) : DaqConfigReadResult;
    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqConfigReadResult;
}
