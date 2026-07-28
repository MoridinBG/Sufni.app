using Sufni.App.Bikes.Models;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Services;

public sealed record BikeAnalysisPresentationData(
    CoordinateList LeverageRatioData,
    CoordinateList? RearAxlePathData);

public abstract record BikeEditorAnalysisResult
{
    private BikeEditorAnalysisResult() { }

    public sealed record Computed(BikeAnalysisPresentationData Data) : BikeEditorAnalysisResult;
    public sealed record Unavailable : BikeEditorAnalysisResult;
    public sealed record Failed(string ErrorMessage) : BikeEditorAnalysisResult;
}

public abstract record BikeImageLoadResult
{
    private BikeImageLoadResult() { }

    public sealed record Loaded(byte[] ImageBytes, string FileName) : BikeImageLoadResult;
    public sealed record Canceled : BikeImageLoadResult;
    public sealed record Failed(string ErrorMessage) : BikeImageLoadResult;
}

public abstract record BikeFileImportResult
{
    private BikeFileImportResult() { }

    public sealed record Imported(Bike Bike) : BikeFileImportResult;
    public sealed record Canceled : BikeFileImportResult;
    public sealed record InvalidFile(string ErrorMessage) : BikeFileImportResult;
    public sealed record Failed(string ErrorMessage) : BikeFileImportResult;
}

public abstract record BikeExportResult
{
    private BikeExportResult() { }

    public sealed record Exported : BikeExportResult;
    public sealed record Canceled : BikeExportResult;
    public sealed record Failed(string ErrorMessage) : BikeExportResult;
}

public abstract record LeverageRatioImportResult
{
    private LeverageRatioImportResult() { }

    public sealed record Imported(LeverageRatioSpec Value) : LeverageRatioImportResult;
    public sealed record Canceled : LeverageRatioImportResult;
    public sealed record Invalid(string[] ErrorMessages) : LeverageRatioImportResult;
    public sealed record Failed(string ErrorMessage) : LeverageRatioImportResult;
}
