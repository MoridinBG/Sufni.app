using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Database;

public interface IExtensionStateRefreshParticipant
{
    Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default);
}

