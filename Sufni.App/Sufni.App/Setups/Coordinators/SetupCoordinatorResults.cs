using System;
using Sufni.App.Shared.Models;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Setups.Coordinators;

public sealed record ImportedSetupEditorData(
    SetupSnapshot Setup,
    BikeSnapshot Bike,
    Guid? BoardId = null);

public abstract record SetupImportResult
{
    private SetupImportResult() { }

    public sealed record Imported(ImportedSetupEditorData Data) : SetupImportResult;
    public sealed record Canceled : SetupImportResult;
    public sealed record BoardInUse(Guid BoardId) : SetupImportResult;
    public sealed record InvalidFile(string ErrorMessage) : SetupImportResult;
    public sealed record Failed(SetupImportFailureReason Reason, string? Details = null) : SetupImportResult;
}

