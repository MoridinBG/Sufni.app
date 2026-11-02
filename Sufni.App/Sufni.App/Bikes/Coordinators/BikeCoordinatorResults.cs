using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Models;
using Sufni.App.Bikes.Services;

namespace Sufni.App.Bikes.Coordinators;

public abstract record BikeImportResult
{
    private BikeImportResult() { }

    public sealed record Imported(BikeDraft Draft) : BikeImportResult;
    public sealed record Canceled : BikeImportResult;
    public sealed record InvalidFile(string ErrorMessage) : BikeImportResult;
    public sealed record Failed(BikeImportFailureReason Reason, string? Details = null) : BikeImportResult;
}

public abstract record BikeDraftLoadResult
{
    private BikeDraftLoadResult() { }
    public sealed record Loaded(BikeDraft Draft) : BikeDraftLoadResult;
    public sealed record Missing : BikeDraftLoadResult;
    public sealed record Failed(BikeLoadFailureReason Reason, string? Details = null) : BikeDraftLoadResult;
}
