using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.ExtensionHost.Database;

internal sealed class ExtensionDatabaseMigratorRunner
{
    private readonly IReadOnlyList<IExtensionDatabaseMigrator> migrators;
    private readonly ExtensionDatabaseTableCatalog tableCatalog;

    public ExtensionDatabaseMigratorRunner(IEnumerable<IExtensionDatabaseMigrator> migrators)
    {
        this.migrators = migrators.ToArray();
        tableCatalog = ExtensionDatabaseTableCatalog.Create(this.migrators);
    }

    public async Task RunAsync(SQLiteAsyncConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.CreateTableAsync<ExtensionSchemaVersion>();
        await CreateExtensionTablesAsync(connection);
        await RunMigrationStepsAsync(connection, cancellationToken);
    }

    private async Task CreateExtensionTablesAsync(SQLiteAsyncConnection connection)
    {
        var tableTypes = tableCatalog.TableTypes;

        if (tableTypes.Length == 0)
        {
            return;
        }

        await connection.CreateTablesAsync(CreateFlags.None, tableTypes);
    }

    private async Task RunMigrationStepsAsync(SQLiteAsyncConnection connection, CancellationToken cancellationToken)
    {
        foreach (var migrator in migrators)
        {
            var version = await connection.FindAsync<ExtensionSchemaVersion>(migrator.ExtensionId);
            var currentVersion = version?.Version ?? 0;
            var context = new ExtensionDatabaseMigrationContext(
                migrator.ExtensionId,
                new ExtensionDatabaseSession(connection, tableCatalog));
            var pendingSteps = migrator.Steps
                .Where(step => step.TargetVersion > currentVersion && step.TargetVersion <= migrator.TargetVersion)
                .OrderBy(step => step.TargetVersion)
                .ToArray();

            foreach (var step in pendingSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await step.ApplyAsync(context, cancellationToken);
                await connection.InsertOrReplaceAsync(new ExtensionSchemaVersion
                {
                    ExtensionId = migrator.ExtensionId,
                    Version = step.TargetVersion
                });
            }
        }
    }
}
