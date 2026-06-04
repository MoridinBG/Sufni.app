using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Database;

public interface IExtensionCascadeService
{
    Task ApplyForDeletedCoreEntityAsync(
        ExtensionCoreEntityKind kind,
        Guid id,
        CancellationToken cancellationToken = default);

    Task RepairOrphansAsync(CancellationToken cancellationToken = default);
}

