namespace Sufni.App.Services.Management;

public abstract record DaqGetFileResult
{
    private DaqGetFileResult()
    {
    }

    public sealed record Downloaded(string Name, ulong FileSizeBytes) : DaqGetFileResult;

    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqGetFileResult;
}
