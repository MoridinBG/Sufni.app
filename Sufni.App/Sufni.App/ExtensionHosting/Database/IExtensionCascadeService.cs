using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.ExtensionHost.Contracts.Database;

namespace Sufni.App.ExtensionHosting.Database;

public interface IExtensionCascadeService
{
    Task ApplyForDeletedCoreEntityAsync(
        ExtensionCoreEntityKind kind,
        Guid id,
        CancellationToken cancellationToken = default);

    Task RepairOrphansAsync(CancellationToken cancellationToken = default);
}

