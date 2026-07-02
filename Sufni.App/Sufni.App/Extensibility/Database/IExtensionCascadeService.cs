using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Database;
using SQLite;

namespace Sufni.App.Extensibility.Database;

public interface IExtensionCascadeService
{
    bool ApplyRulesForDeletedCoreEntityInTransaction(
        SQLiteConnection connection,
        ExtensionCoreEntityKind kind,
        Guid id);

    Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default);

    Task RepairOrphansAsync(CancellationToken cancellationToken = default);
}
