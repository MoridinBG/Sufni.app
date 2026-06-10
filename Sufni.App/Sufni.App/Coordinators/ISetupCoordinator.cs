using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.SetupEditing;

namespace Sufni.App.Coordinators;

public interface ISetupCoordinator
{
    Task OpenCreateAsync(Guid? suggestedBoardId = null);

    Task OpenCreateForDetectedBoardAsync();

    Task OpenEditAsync(Guid setupId);

    Task<SetupSaveResult> SaveAsync(Setup setup, Guid? boardId, long baselineUpdated);

    Task<SetupDeleteResult> DeleteAsync(Guid setupId);

    Task<SetupImportResult> ImportSetupAsync(CancellationToken cancellationToken = default);

    Task<SetupExportResult> ExportSetupAsync(
        Setup setup,
        Bike bike,
        Guid? boardId,
        CancellationToken cancellationToken = default);
}
