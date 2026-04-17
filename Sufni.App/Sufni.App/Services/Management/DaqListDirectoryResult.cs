namespace Sufni.App.Services.Management;

public abstract record DaqListDirectoryResult
{
    private DaqListDirectoryResult()
    {
    }

    public sealed record Listed(DaqDirectoryRecord Directory) : DaqListDirectoryResult;

    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqListDirectoryResult;
}