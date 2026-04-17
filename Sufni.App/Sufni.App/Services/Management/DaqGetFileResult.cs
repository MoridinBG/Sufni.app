namespace Sufni.App.Services.Management;

public abstract record DaqGetFileResult
{
    private DaqGetFileResult()
    {
    }

    public sealed record Loaded(string Name, byte[] Bytes) : DaqGetFileResult;

    public sealed record Error(DaqManagementErrorCode ErrorCode, string Message) : DaqGetFileResult;
}