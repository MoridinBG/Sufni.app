using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Shared.Models;

namespace Sufni.App.Bikes.Queries;

public interface IBikeDraftLoader
{
    Task<BikeDraftLoadResult> LoadDraftAsync(Guid bikeId, CancellationToken cancellationToken = default);
}

public closed record BikeDraftLoadResult
{
    private BikeDraftLoadResult() { }
    public sealed record Loaded(BikeDraft Draft) : BikeDraftLoadResult;
    public sealed record Missing : BikeDraftLoadResult;
    public sealed record Failed(BikeLoadFailureReason Reason, string? Details = null) : BikeDraftLoadResult;
}
