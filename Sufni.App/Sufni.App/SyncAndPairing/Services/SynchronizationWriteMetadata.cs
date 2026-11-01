using System;
using System.Diagnostics.CodeAnalysis;
using SQLite;
using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Services;

internal static class SynchronizationWriteMetadata
{
    public static long NextLocalSequence(SQLiteConnection connection)
    {
        connection.Execute("UPDATE sync_sequence SET local_seq = local_seq + 1 WHERE id = 1");
        return connection.ExecuteScalar<long>("SELECT local_seq FROM sync_sequence WHERE id = 1");
    }

    public static long NextSyncSequence(SQLiteConnection connection)
    {
        connection.Execute("UPDATE sync_sequence SET sync_seq = sync_seq + 1 WHERE id = 1");
        return connection.ExecuteScalar<long>("SELECT sync_seq FROM sync_sequence WHERE id = 1");
    }

    public static void PrepareAuthoredWrite<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection, T row, long now, string writer) where T : Synchronizable, new()
    {
        var table = PersistenceGuards.GetTableName<T>();
        var currentVersion = connection.ExecuteScalar<long>($"SELECT version FROM {table} WHERE id = ?", row.Id);
        var deletedVersion = connection.ExecuteScalar<long>(
            "SELECT version FROM sync_deletion WHERE entity_table = ? AND id = ?", table, row.Id);
        row.Version = Math.Max(currentVersion, deletedVersion) + 1;
        row.Updated = now;
        row.UpdatedBy = writer;
        row.LocalSequence = NextLocalSequence(connection);
        row.SyncSequence = NextSyncSequence(connection);
    }

    public static void StampAuthoredChange<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection, Guid id, long now, string writer) where T : Synchronizable, new()
    {
        var row = new T { Id = id };
        PrepareAuthoredWrite(connection, row, now, writer);
        connection.Execute($"UPDATE {PersistenceGuards.GetTableName<T>()} SET version=?, updated=?, updated_by=?, local_seq=?, sync_seq=? WHERE id=?",
            row.Version, row.Updated, row.UpdatedBy, row.LocalSequence, row.SyncSequence, id);
    }

    public static void StampDerivedChange<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection, Guid id) where T : Synchronizable, new() =>
        connection.Execute($"UPDATE {PersistenceGuards.GetTableName<T>()} SET local_seq=?, sync_seq=? WHERE id=?",
            NextLocalSequence(connection), NextSyncSequence(connection), id);

    public static void RecordDeletion<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection, T row) where T : Synchronizable, new() =>
        RecordDeletion(connection, SynchronizationDeletion.From(row, PersistenceGuards.GetTableName<T>()));

    public static void RecordDeletion(SQLiteConnection connection, SynchronizationDeletion row) =>
        connection.Execute("""
            INSERT INTO sync_deletion (entity_table, id, version, updated, updated_by, deleted, local_seq, sync_seq)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(entity_table, id) DO UPDATE SET version=excluded.version, updated=excluded.updated,
                updated_by=excluded.updated_by, deleted=excluded.deleted, local_seq=excluded.local_seq, sync_seq=excluded.sync_seq
            """, row.EntityTable, row.Id, row.Version, row.Updated, row.UpdatedBy,
            row.Deleted, row.LocalSequence, row.SyncSequence);
}
