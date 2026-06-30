using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.Editors;
namespace Sufni.App.Bikes.Coordinators;

public interface IBikeCoordinator
{
    Task OpenCreateAsync();

    Task OpenEditAsync(Guid bikeId);

    Task<BikeEditorAnalysisResult> LoadAnalysisAsync(
        RearSuspension? rearSuspension,
        CancellationToken cancellationToken = default);

    Task<BikeImageLoadResult> LoadImageAsync(CancellationToken cancellationToken = default);

    Task<BikeImportResult> ImportBikeAsync(CancellationToken cancellationToken = default);

    Task<LeverageRatioImportResult> ImportLeverageRatioAsync(CancellationToken cancellationToken = default);

    Task<BikeExportResult> ExportBikeAsync(Bike bike, CancellationToken cancellationToken = default);

    Task<BikeSaveResult> SaveAsync(Bike bike, long baselineUpdated);

    Task<BikeDampingSpeedCutoffUpdateResult> UpdateDampingSpeedCutoffAsync(
        Guid bikeId,
        long baselineUpdated,
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond);

    Task<BikeDeleteResult> DeleteAsync(Guid bikeId);
}
