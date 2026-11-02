using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
namespace Sufni.App.Bikes.Coordinators;

public interface IBikeCoordinator
{
    Task<BikeDraftLoadResult> LoadDraftAsync(Guid bikeId, CancellationToken cancellationToken = default);

    Task<BikeEditorAnalysisResult> LoadAnalysisAsync(
        RearSuspensionSpec rearSuspension,
        CancellationToken cancellationToken = default);

    Task<BikeImageLoadResult> LoadImageAsync(CancellationToken cancellationToken = default);

    Task<BikeImportResult> ImportBikeAsync(CancellationToken cancellationToken = default);

    Task<LeverageRatioImportResult> ImportLeverageRatioAsync(CancellationToken cancellationToken = default);

    Task<BikeExportResult> ExportBikeAsync(BikeDraft draft, CancellationToken cancellationToken = default);

    Task<BikeSaveResult> SaveAsync(BikeDraft draft, BikeDraft? draftBase);

    Task<BikeDampingSpeedCutoffUpdateResult> UpdateDampingSpeedCutoffAsync(
        Guid bikeId,
        double expectedCutoffMmPerSecond,
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond);

    Task<BikeDeleteResult> DeleteAsync(Guid bikeId);
}
