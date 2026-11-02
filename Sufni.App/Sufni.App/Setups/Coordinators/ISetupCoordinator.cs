using Sufni.App.Setups.Services;
using Sufni.App.Setups.Queries;
using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Editors;
namespace Sufni.App.Setups.Coordinators;

public interface ISetupCoordinator
{
    SetupDetails CreateDraft(Guid? suggestedBoardId = null);

    Task<SetupSaveResult> SaveAsync(SetupDraft draft, SetupDraft? draftBase);

    Task<SetupDeleteResult> DeleteAsync(Guid setupId);

    Task<SetupImportResult> ImportSetupAsync(CancellationToken cancellationToken = default);

    Task<SetupExportResult> ExportSetupAsync(SetupDraft draft, CancellationToken cancellationToken = default);
}
