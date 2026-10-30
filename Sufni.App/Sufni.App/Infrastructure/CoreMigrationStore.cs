using System;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.Infrastructure;

// Records completed startup schema migrations in the same transaction as their work.
internal sealed class CoreMigrationStore(SQLiteAsyncConnection connection)
{
    public Task EnsureTableAsync() => connection.ExecuteAsync(
        "CREATE TABLE IF NOT EXISTS core_migration (id TEXT PRIMARY KEY)");

    public async Task<bool> IsAppliedAsync(string migrationId)
    {
        var rows = await connection.QueryAsync<CoreMigrationRow>(
            "SELECT id FROM core_migration WHERE id = ?",
            migrationId);
        return rows.Count > 0;
    }

    public Task MarkAppliedAsync(string migrationId) => connection.ExecuteAsync(
        "INSERT OR IGNORE INTO core_migration (id) VALUES (?)",
        migrationId);

    public Task ApplyAsync(string migrationId, Action<SQLiteConnection> migrate) =>
        connection.RunInTransactionAsync(transaction =>
        {
            if (transaction.ExecuteScalar<int>("SELECT COUNT(*) FROM core_migration WHERE id = ?", migrationId) != 0)
            {
                return;
            }

            migrate(transaction);
            transaction.Execute("INSERT INTO core_migration (id) VALUES (?)", migrationId);
        });

    private sealed class CoreMigrationRow
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;
    }
}
