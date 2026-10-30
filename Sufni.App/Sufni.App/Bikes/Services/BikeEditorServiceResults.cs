using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Models;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Services;

public sealed record BikeAnalysisPresentationData(
    CoordinateList LeverageRatioData,
    CoordinateList? RearAxlePathData);

public abstract record BikeEditorAnalysisResult
{
    private BikeEditorAnalysisResult() { }

    public sealed record Computed(BikeAnalysisPresentationData Data) : BikeEditorAnalysisResult;
    public sealed record NotApplicable : BikeEditorAnalysisResult;
    public sealed record Unsolvable : BikeEditorAnalysisResult;
    public sealed record Failed(BikeAnalysisFailureReason Reason, string? Details = null) : BikeEditorAnalysisResult;
}

public abstract record BikeImageLoadResult
{
    private BikeImageLoadResult() { }

    public sealed record Loaded(byte[] ImageBytes, string FileName) : BikeImageLoadResult;
    public sealed record Canceled : BikeImageLoadResult;
    public sealed record Failed(BikeImageFailureReason Reason, string? Details = null) : BikeImageLoadResult;
}

public abstract record BikeFileImportResult
{
    private BikeFileImportResult() { }

    public sealed record Imported(Bike Bike) : BikeFileImportResult;
    public sealed record Canceled : BikeFileImportResult;
    public sealed record InvalidFile(string ErrorMessage) : BikeFileImportResult;
    public sealed record Failed(BikeImportFailureReason Reason, string? Details = null) : BikeFileImportResult;
}

public abstract record BikeExportResult
{
    private BikeExportResult() { }

    public sealed record Exported : BikeExportResult;
    public sealed record Canceled : BikeExportResult;
    public sealed record Failed(BikeExportFailureReason Reason, string? Details = null) : BikeExportResult;
}

public abstract record LeverageRatioImportResult
{
    private LeverageRatioImportResult() { }

    public sealed record Imported(LeverageRatioSpec Value) : LeverageRatioImportResult;
    public sealed record Canceled : LeverageRatioImportResult;
    public sealed record Invalid(string[] ErrorMessages) : LeverageRatioImportResult;
    public sealed record Failed(LeverageRatioImportFailureReason Reason, string? Details = null) : LeverageRatioImportResult;
}
