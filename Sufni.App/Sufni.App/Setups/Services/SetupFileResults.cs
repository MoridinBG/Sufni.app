using Sufni.App.Shared.Models;

namespace Sufni.App.Setups.Services;

public abstract record SetupFileReadResult
{
    private SetupFileReadResult() { }
    public sealed record Read(SetupImportPayload Payload) : SetupFileReadResult;
    public sealed record Canceled : SetupFileReadResult;
    public sealed record InvalidFile(string ErrorMessage) : SetupFileReadResult;
    public sealed record Failed(SetupImportFailureReason Reason, string? Details = null) : SetupFileReadResult;
}

public abstract record SetupExportResult
{
    private SetupExportResult() { }

    public sealed record Exported : SetupExportResult;
    public sealed record Canceled : SetupExportResult;
    public sealed record Failed(SetupExportFailureReason Reason, string? Details = null) : SetupExportResult;
}
