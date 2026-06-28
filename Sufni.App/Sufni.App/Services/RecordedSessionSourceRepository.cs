using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Models;
using Sufni.App.SessionGraph;

namespace Sufni.App.Services;

public interface IRecordedSessionSourceRepository
{
    Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync();

    Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id);

    Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync();

    // Session ids that own a recorded-source row (ids only; the payload BLOBs
    // are not loaded). Used by the startup normalization pass to find the
    // source-backed sessions it can recompute.
    Task<List<Guid>> GetSourceBackedSessionIdsAsync();

    Task PutRecordedSessionSourceAsync(RecordedSessionSource source);

    Task DeleteRecordedSessionSourceAsync(Guid sessionId);
}

internal sealed class RecordedSessionSourceRepository(SqliteConnectionContext connectionContext)
    : IRecordedSessionSourceRepository
{
    private const string SessionProcessingFingerprintColumn = "session_processing_fingerprint";

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

    public async Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var query = $"""
                     SELECT
                         s.id,
                         s.{SessionProcessingFingerprintColumn},
                         source.source_hash
                     FROM session s
                     LEFT JOIN session_recording_source source ON source.session_id = s.id
                     WHERE s.deleted IS null
                     """;
        var rows = await connection.QueryAsync<SessionSourceStatusRow>(query);
        return
        [
            .. rows
                .Where(row =>
                {
                    if (string.IsNullOrWhiteSpace(row.SourceHash))
                    {
                        return true;
                    }

                    var expectedSourceHash = TryReadFingerprintSourceHash(row.ProcessingFingerprintJson);
                    return !string.IsNullOrWhiteSpace(expectedSourceHash) &&
                           !StringComparer.Ordinal.Equals(row.SourceHash, expectedSourceHash);
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

    public async Task PutRecordedSessionSourceAsync(RecordedSessionSource source)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await PutRecordedSessionSourceInCurrentTransactionAsync(connection, source);
    }

    public async Task DeleteRecordedSessionSourceAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await connection.ExecuteAsync("DELETE FROM session_recording_source WHERE session_id=?", sessionId);
    }

    internal static Task PutRecordedSessionSourceInCurrentTransactionAsync(
        SQLiteAsyncConnection connection,
        RecordedSessionSource source)
    {
        ValidateRecordedSessionSource(source);

        const string query = """
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

        return connection.ExecuteAsync(query,
            [
                source.SessionId,
                source.SourceKindValue,
                source.SourceName,
                source.SchemaVersion,
                source.SourceHash,
                source.Payload
            ]);
    }

    private static void ValidateRecordedSessionSource(RecordedSessionSource source)
    {
        if (!RecordedSessionSourceHash.Matches(source))
        {
            throw new InvalidOperationException("Recorded session source hash does not match its payload.");
        }
    }

    private static string? TryReadFingerprintSourceHash(string? processingFingerprintJson)
    {
        if (string.IsNullOrWhiteSpace(processingFingerprintJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(processingFingerprintJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "SourceHash", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private sealed class SourceSessionIdRow
    {
        [Column("session_id")]
        public Guid SessionId { get; set; }
    }

    private sealed class SessionSourceStatusRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("session_processing_fingerprint")]
        public string? ProcessingFingerprintJson { get; set; }

        [Column("source_hash")]
        public string? SourceHash { get; set; }
    }
}
