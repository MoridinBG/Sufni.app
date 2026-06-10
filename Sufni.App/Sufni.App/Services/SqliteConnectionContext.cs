using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHosting.Database;

namespace Sufni.App.Services;

internal sealed class SqliteConnectionContext
{
    internal SqliteConnectionContext(
        string databasePath,
        bool createAppDirectories,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators,
        IEnumerable<IExtensionCascadeRuleProvider> extensionCascadeRuleProviders,
        Func<IReadOnlyList<IExtensionStateRefreshParticipant>> extensionStateRefreshParticipantsProvider)
    {
        var extensionMigratorList = extensionMigrators.ToArray();
        var extensionCascadeRuleProviderList = extensionCascadeRuleProviders.ToArray();

        if (createAppDirectories)
        {
            AppPaths.CreateRequiredDirectories();
        }
        else
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        Connection = new SQLiteAsyncConnection(databasePath);
        ExtensionTableCatalog = ExtensionDatabaseTableCatalog.Create(extensionMigratorList);
        var extensionCascadeService = new ExtensionCascadeService(
            Connection,
            extensionMigratorList,
            extensionCascadeRuleProviderList,
            extensionStateRefreshParticipantsProvider);
        var migrationRunner = new DatabaseMigrationRunner(
            databasePath,
            Connection,
            new ExtensionDatabaseMigratorRunner(extensionMigratorList),
            extensionCascadeService);
        Initialization = migrationRunner.RunAsync();
    }

    internal SQLiteAsyncConnection Connection { get; }

    internal ExtensionDatabaseTableCatalog ExtensionTableCatalog { get; }

    internal Task Initialization { get; }

    internal async Task<SQLiteAsyncConnection> GetInitializedConnectionAsync(CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        return Connection;
    }
}
