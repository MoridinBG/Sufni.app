using Avalonia.Media.Imaging;
using Sufni.App.Models;
using Sufni.Kinematics;

namespace Sufni.App.BikeEditing;

public sealed record BikeAnalysisPresentationData(CoordinateList LeverageRatioData);

public sealed record ImportedBikeEditorData(Bike Bike, BikeEditorAnalysisResult AnalysisResult);

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

    public sealed record Loaded(Bitmap Bitmap) : BikeImageLoadResult;
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

public abstract record BikeImportResult
{
    private BikeImportResult() { }

    public sealed record Imported(ImportedBikeEditorData Data) : BikeImportResult;
    public sealed record Canceled : BikeImportResult;
    public sealed record InvalidFile(string ErrorMessage) : BikeImportResult;
    public sealed record Failed(string ErrorMessage) : BikeImportResult;
}

public abstract record BikeExportResult
{
    private BikeExportResult() { }

    public sealed record Exported : BikeExportResult;
    public sealed record Canceled : BikeExportResult;
    public sealed record Failed(string ErrorMessage) : BikeExportResult;
}