using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SQLite;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Services;

public interface IRecordedSessionSourceRepository
{
    Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync();

    Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id);

    Task<List<RecordedSessionSourceSnapshot>> GetRecordedSessionSourceSnapshotsAsync();

    Task<RecordedSessionSourceSnapshot?> GetRecordedSessionSourceSnapshotAsync(Guid id);

    Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync();

    // Session ids that own a recorded-source row (ids only; the payload BLOBs
    // are not loaded). Used by the startup normalization pass to find the
    // source-backed sessions it can recompute.
    Task<List<Guid>> GetSourceBackedSessionIdsAsync();

    Task<List<Guid>> GetPersistedDerivationSourceSessionIdsAsync();

    Task PutRecordedSessionSourceAsync(RecordedSessionSource source);

    Task DeleteRecordedSessionSourceAsync(Guid sessionId);

    Task<int> DeleteOrphanedRecordedSessionSourcesAsync(IReadOnlyCollection<Guid> retainedSourceSessionIds);
}

internal sealed class RecordedSessionSourceRepository(SqliteConnectionContext connectionContext)
    : IRecordedSessionSourceRepository
{
    private const string SessionProcessingFingerprintColumn = "session_processing_fingerprint";
    private const int RetainedSourceInsertChunkSize = 500;
    private const string RetainedSourceTempTable = "retained_recorded_session_source_ids";
    private const string PutRecordedSessionSourceSql = """
                                                       INSERT OR REPLACE INTO session_recording_source (
                                                           session_id,
                                                           source_kind,
                                                           source_name,
                                                           schema_version,
                                                           source_hash,
                                                           payload
                                                       )
                                                       VALUES (?, ?, ?, ?, ?, ?)
                                                       """;

    public async Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<RecordedSessionSource>().ToListAsync();
    }

    public async Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<RecordedSessionSource>()
            .Where(source => source.SessionId == id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<RecordedSessionSourceSnapshot>> GetRecordedSessionSourceSnapshotsAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<RecordedSessionSourceSnapshotRow>(
            """
            SELECT session_id, source_kind, source_name, schema_version, source_hash
            FROM session_recording_source
            """);
        return rows.Select(row => row.ToSnapshot()).ToList();
    }

    public async Task<RecordedSessionSourceSnapshot?> GetRecordedSessionSourceSnapshotAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<RecordedSessionSourceSnapshotRow>(
            """
            SELECT session_id, source_kind, source_name, schema_version, source_hash
            FROM session_recording_source
            WHERE session_id = ?
            """,
            id);
        return rows.Count == 1 ? rows[0].ToSnapshot() : null;
    }

    public async Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var query = $"""
                     SELECT
                         s.id,
                         s.{SessionProcessingFingerprintColumn}
                     FROM session s
                     WHERE s.deleted IS null
                     """;
        var sourceHashRows = await connection.QueryAsync<RecordedSessionSourceHashRow>(
            "SELECT session_id, source_hash FROM session_recording_source");
        var sourceHashesBySessionId = sourceHashRows.ToDictionary(row => row.SessionId, row => row.SourceHash);
        var rows = await connection.QueryAsync<SessionSourceStatusRow>(query);
        return
        [
            .. rows
                .Where(row =>
                {
                    var persistedFingerprint = TryReadProcessingFingerprint(row.ProcessingFingerprintJson);
                    var sourceSessionId = persistedFingerprint?.DerivationWindow?.SourceSessionId ?? row.Id;
                    sourceHashesBySessionId.TryGetValue(sourceSessionId, out var sourceHash);
                    return RecordedSessionSourceCompleteness.IsSourceMissingOrHashMismatch(
                        sourceHash,
                        persistedFingerprint);
                })
                .Select(row => row.Id)
        ];
    }

    public async Task<List<Guid>> GetSourceBackedSessionIdsAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<SourceSessionIdRow>(
            "SELECT session_id FROM session_recording_source");
        return [.. rows.Select(row => row.SessionId)];
    }

    public async Task<List<Guid>> GetPersistedDerivationSourceSessionIdsAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<SessionFingerprintSourceRow>(
            $"""
             SELECT id, {SessionProcessingFingerprintColumn}
             FROM session
             WHERE deleted IS null
               AND {SessionProcessingFingerprintColumn} IS NOT null
             """);

        return
        [
            .. rows
                .Select(row => new
                {
                    row.Id,
                    SourceSessionId = RecordedSessionDerivationResolver.GetEffectiveSourceSessionId(
                        row.Id,
                        TryReadFingerprint(row.ProcessingFingerprintJson))
                })
                .Where(row => row.SourceSessionId != row.Id)
                .Select(row => row.SourceSessionId)
                .Distinct()
        ];
    }

    public async Task PutRecordedSessionSourceAsync(RecordedSessionSource source)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        ValidateRecordedSessionSource(source);
        await connection.ExecuteAsync(PutRecordedSessionSourceSql, CreatePutRecordedSessionSourceValues(source));
    }

    public async Task DeleteRecordedSessionSourceAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await DeleteRecordedSessionSourceAsync(connection, sessionId);
    }

    public async Task<int> DeleteOrphanedRecordedSessionSourcesAsync(IReadOnlyCollection<Guid> retainedSourceSessionIds)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        if (retainedSourceSessionIds.Count == 0)
        {
            return await connection.ExecuteAsync(
                "DELETE FROM session_recording_source WHERE session_id NOT IN (SELECT id FROM session)");
        }

        return await connectionContext.RunInTransactionAsync(connection =>
        {
            connection.Execute($"DROP TABLE IF EXISTS {RetainedSourceTempTable}");
            connection.Execute($"CREATE TEMP TABLE {RetainedSourceTempTable}(id TEXT PRIMARY KEY)");
            try
            {
                InsertRetainedSourceIds(connection, retainedSourceSessionIds);
                return connection.Execute(
                    $"""
                     DELETE FROM session_recording_source
                     WHERE session_id NOT IN (SELECT id FROM session)
                       AND NOT EXISTS (
                           SELECT 1
                           FROM {RetainedSourceTempTable} retained
                           WHERE retained.id = session_recording_source.session_id
                       )
                     """);
            }
            finally
            {
                connection.Execute($"DROP TABLE IF EXISTS {RetainedSourceTempTable}");
            }
        });
    }

    internal static int PutRecordedSessionSourceInTransaction(
        SQLiteConnection connection,
        RecordedSessionSource source)
    {
        ValidateRecordedSessionSource(source);
        return connection.Execute(PutRecordedSessionSourceSql, CreatePutRecordedSessionSourceValues(source));
    }

    internal static int DeleteRecordedSessionSourceInTransaction(
        SQLiteConnection connection,
        Guid sessionId) =>
        DeleteRecordedSessionSource(connection, sessionId);

    private static int DeleteRecordedSessionSource(
        SQLiteConnection connection,
        Guid sessionId) =>
        connection.Execute("DELETE FROM session_recording_source WHERE session_id=?", sessionId);

    private static Task<int> DeleteRecordedSessionSourceAsync(
        SQLiteAsyncConnection connection,
        Guid sessionId) =>
        connection.ExecuteAsync("DELETE FROM session_recording_source WHERE session_id=?", sessionId);

    private static object?[] CreatePutRecordedSessionSourceValues(RecordedSessionSource source) =>
    [
        source.SessionId,
        source.SourceKindValue,
        source.SourceName,
        source.SchemaVersion,
        source.SourceHash,
        source.Payload
    ];

    private static void InsertRetainedSourceIds(
        SQLiteConnection connection,
        IReadOnlyCollection<Guid> retainedSourceSessionIds)
    {
        foreach (var chunk in retainedSourceSessionIds.Distinct().Chunk(RetainedSourceInsertChunkSize))
        {
            var placeholders = string.Join(", ", chunk.Select(_ => "(?)"));
            var values = chunk.Select(id => (object)id.ToString("D")).ToArray();
            connection.Execute(
                $"INSERT OR IGNORE INTO {RetainedSourceTempTable}(id) VALUES {placeholders}",
                values);
        }
    }

    private static void ValidateRecordedSessionSource(RecordedSessionSource source)
    {
        if (!RecordedSessionSourceHash.Matches(source))
        {
            throw new InvalidOperationException("Recorded session source hash does not match its payload.");
        }
    }

    private static ProcessingFingerprint? TryReadProcessingFingerprint(string? processingFingerprintJson)
    {
        if (string.IsNullOrWhiteSpace(processingFingerprintJson))
        {
            return null;
        }

        try
        {
            return AppJson.Deserialize<ProcessingFingerprint>(processingFingerprintJson);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static ProcessingFingerprint? TryReadFingerprint(string? processingFingerprintJson)
    {
        if (string.IsNullOrWhiteSpace(processingFingerprintJson))
        {
            return null;
        }

        try
        {
            return AppJson.Deserialize<ProcessingFingerprint>(processingFingerprintJson);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class SourceSessionIdRow
    {
        [Column("session_id")]
        public Guid SessionId { get; set; }
    }

    private sealed class RecordedSessionSourceHashRow
    {
        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("source_hash")]
        public string SourceHash { get; set; } = null!;
    }

    private sealed class RecordedSessionSourceSnapshotRow
    {
        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("source_kind")]
        public string SourceKindValue { get; set; } = null!;

        [Column("source_name")]
        public string SourceName { get; set; } = null!;

        [Column("schema_version")]
        public int SchemaVersion { get; set; }

        [Column("source_hash")]
        public string SourceHash { get; set; } = null!;

        public RecordedSessionSourceSnapshot ToSnapshot() => new(
            SessionId,
            RecordedSessionSourceKindExtensions.FromStorageValue(SourceKindValue),
            SourceName,
            SchemaVersion,
            SourceHash);
    }

    private sealed class SessionSourceStatusRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("session_processing_fingerprint")]
        public string? ProcessingFingerprintJson { get; set; }
    }

    private sealed class SessionFingerprintSourceRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("session_processing_fingerprint")]
        public string? ProcessingFingerprintJson { get; set; }
    }
}
