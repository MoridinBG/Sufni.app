using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;

namespace Sufni.App.Bikes.Coordinators;

public sealed record ImportedBikeEditorData(Bike Bike, BikeEditorAnalysisResult AnalysisResult);

public abstract record BikeImportResult
{
    private BikeImportResult() { }

    public sealed record Imported(ImportedBikeEditorData Data) : BikeImportResult;
    public sealed record Canceled : BikeImportResult;
    public sealed record InvalidFile(string ErrorMessage) : BikeImportResult;
    public sealed record Failed(string ErrorMessage) : BikeImportResult;
}
