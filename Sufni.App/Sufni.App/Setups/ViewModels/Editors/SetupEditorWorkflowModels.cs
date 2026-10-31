using System;
using Sufni.App.Shared.Models;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Setups.ViewModels.Editors;

public sealed record ImportedSetupEditorData(
    SetupSnapshot Setup,
    BikeSnapshot Bike,
    string? BoardIdWarning,
    Guid? BoardId = null);

public abstract record SetupImportResult
{
    private SetupImportResult() { }

    public sealed record Imported(ImportedSetupEditorData Data) : SetupImportResult;
    public sealed record Canceled : SetupImportResult;
    public sealed record InvalidFile(string ErrorMessage) : SetupImportResult;
    public sealed record Failed(SetupImportFailureReason Reason, string? Details = null) : SetupImportResult;
}

public abstract record SetupExportResult
{
    private SetupExportResult() { }

    public sealed record Exported : SetupExportResult;
    public sealed record Canceled : SetupExportResult;
    public sealed record Failed(SetupExportFailureReason Reason, string? Details = null) : SetupExportResult;
}
