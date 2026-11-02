using System;
using Sufni.App.Shared.Models;

namespace Sufni.App.Setups.Coordinators;

public abstract record SetupImportResult
{
    private SetupImportResult() { }

    public sealed record Imported(Guid SetupId) : SetupImportResult;
    public sealed record Canceled : SetupImportResult;
    public sealed record BoardInUse(Guid BoardId) : SetupImportResult;
    public sealed record InvalidFile(string ErrorMessage) : SetupImportResult;
    public sealed record Failed(SetupImportFailureReason Reason, string? Details = null) : SetupImportResult;
}

