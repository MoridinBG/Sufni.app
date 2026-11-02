using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;

namespace Sufni.App.Setups.Services;

public interface ISetupFileService
{
    Task<SetupFileReadResult> ReadAsync(CancellationToken cancellationToken = default);
    Task<SetupExportResult> ExportAsync(Setup setup, Bike bike, Guid? boardId, CancellationToken cancellationToken = default);
}
