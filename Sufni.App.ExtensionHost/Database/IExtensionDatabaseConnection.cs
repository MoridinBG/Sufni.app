using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.ExtensionHost.Database;

public interface IExtensionDatabaseConnection
{
    Task<SQLiteAsyncConnection> GetInitializedConnectionAsync(CancellationToken cancellationToken = default);
}

