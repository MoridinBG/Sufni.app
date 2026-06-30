using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SQLite;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Services;

/// <summary>
/// Hub-side record of processed BLOBs the hub wants but does not yet hold: rows
/// where the hub keeps an old, deferred BLOB while the synced metadata advertises
/// a newer current-schema fingerprint that matches the hub's current database
/// inputs. Each entry is a (session id, target fingerprint) pair. The merge engine
/// records/clears entries while applying a client push; the sync server advertises
/// them in its incomplete-sessions list and clears them when the matching upload
/// commits as a swap. It is the push-direction analogue of the client's pull-swap
/// set, made durable here because a client push carries no further metadata delta
/// to re-derive it from.
/// </summary>
public interface ISessionBlobSwapRequestStore
{
    Task<List<Guid>> GetRequestedSessionIdsAsync();

    Task<string?> GetTargetFingerprintAsync(Guid sessionId);

    Task ClearAsync(Guid sessionId);
}

internal sealed class SessionBlobSwapRequestStore(SqliteConnectionContext connectionContext)
    : ISessionBlobSwapRequestStore
{
    // Single home for the table SQL so the startup migrator (which creates it) and
    // the merge engine's transactional writes reference the same definition.
    public const string TableName = "session_blob_swap_request";

    public const string CreateTableSql =
        $"CREATE TABLE IF NOT EXISTS {TableName} (session_id TEXT PRIMARY KEY, target_fingerprint TEXT NOT NULL)";

    public async Task<List<Guid>> GetRequestedSessionIdsAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<SwapRequestRow>($"SELECT session_id, target_fingerprint FROM {TableName}");
        return rows.Select(row => row.SessionId).ToList();
    }

    public async Task<string?> GetTargetFingerprintAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<SwapRequestRow>(
            $"SELECT session_id, target_fingerprint FROM {TableName} WHERE session_id = ?",
            sessionId);
        return rows.Count == 1 ? rows[0].TargetFingerprint : null;
    }

    public async Task ClearAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await connection.ExecuteAsync($"DELETE FROM {TableName} WHERE session_id = ?", sessionId);
    }

    private sealed class SwapRequestRow
    {
        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("target_fingerprint")]
        public string TargetFingerprint { get; set; } = string.Empty;
    }
}
