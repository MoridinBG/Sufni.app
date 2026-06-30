using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.Infrastructure;

// Single home for the `core_migration` marker table: which one-time, per-device
// migrations have already run. Shared by the startup schema migrator (which
// creates the table) and the one-time normalization pass (which reads/writes
// markers after startup), so the table SQL lives in exactly one place.
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

    private sealed class CoreMigrationRow
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;
    }
}
