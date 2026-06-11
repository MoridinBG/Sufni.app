using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.Database;

public interface IExtensionStateRefreshParticipant
{
    Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default);
}

