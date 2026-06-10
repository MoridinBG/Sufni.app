using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHosting.Database;

namespace Sufni.App.Services;

internal sealed class ExtensionDatabaseConnection(SqliteConnectionContext connectionContext) : IExtensionDatabaseConnection
{
    public async Task<IExtensionDatabaseSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync(cancellationToken);
        return new ExtensionDatabaseSession(connection, connectionContext.ExtensionTableCatalog);
    }
}
