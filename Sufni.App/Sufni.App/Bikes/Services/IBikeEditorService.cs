using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
namespace Sufni.App.Bikes.Services;

public interface IBikeEditorService
{
    Task<BikeImageLoadResult> LoadImageAsync(CancellationToken cancellationToken = default);

    Task<BikeFileImportResult> ImportBikeAsync(CancellationToken cancellationToken = default);

    Task<LeverageRatioImportResult> ImportLeverageRatioAsync(CancellationToken cancellationToken = default);

    Task<BikeExportResult> ExportBikeAsync(Bike bike, CancellationToken cancellationToken = default);
}
