using System.Threading;
using System.Threading.Tasks;
using Sufni.App.BikeEditing;
using Sufni.App.Models;
using Sufni.Kinematics;

namespace Sufni.App.Services;

public interface IBikeEditorService
{
    Task<BikeEditorAnalysisResult> AnalyzeLinkageAsync(
        Linkage? linkage,
        CancellationToken cancellationToken = default);

    Task<BikeImageLoadResult> LoadImageAsync(CancellationToken cancellationToken = default);

    Task<BikeFileImportResult> ImportBikeAsync(CancellationToken cancellationToken = default);

    Task<BikeExportResult> ExportBikeAsync(Bike bike, CancellationToken cancellationToken = default);
}