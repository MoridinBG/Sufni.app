using Sufni.App.Stores;

namespace Sufni.App.SetupEditing;

public sealed record ImportedSetupEditorData(
    SetupSnapshot Setup,
    BikeSnapshot Bike,
    string? BoardIdWarning);

public abstract record SetupImportResult
{
    private SetupImportResult() { }

    public sealed record Imported(ImportedSetupEditorData Data) : SetupImportResult;
    public sealed record Canceled : SetupImportResult;
    public sealed record InvalidFile(string ErrorMessage) : SetupImportResult;
    public sealed record Failed(string ErrorMessage) : SetupImportResult;
}

public abstract record SetupExportResult
{
    private SetupExportResult() { }

    public sealed record Exported : SetupExportResult;
    public sealed record Canceled : SetupExportResult;
    public sealed record Failed(string ErrorMessage) : SetupExportResult;
}
