using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SQLite;

using Sufni.App.SyncAndPairing.Models;
using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.SyncAndPairing.Services;

/// <summary>
/// Synchronized-row persistence helpers for repositories and the merge
/// engine: table-name resolution, existence checks, and validated
/// insert/update entry points. Writes use the declared entity schema, excluding
/// columns that exist only on a read projection.
/// </summary>
internal static class PersistenceGuards
{
    public static string GetTableName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : new()
    {
        return typeof(T).GetCustomAttribute<TableAttribute>()?.Name
               ?? throw new InvalidOperationException($"Type {typeof(T).Name} is missing a SQLite table attribute.");
    }

    public static bool EntityExists<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection,
        Guid id) where T : Synchronizable, new()
    {
        var tableName = GetTableName<T>();
        return connection.ExecuteScalar<int>($"SELECT COUNT(1) FROM {tableName} WHERE id = ?", id) > 0;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "T is annotated to preserve SQLite-mapped members.")]
    public static int InsertEntity<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection,
        T entity) where T : new()
    {
        ValidateEntityForPersistence(entity);
        return connection.Insert(entity, typeof(T));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "T is annotated to preserve SQLite-mapped members.")]
    public static int UpdateEntity<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection,
        T entity) where T : new()
    {
        ValidateEntityForPersistence(entity);
        return connection.Update(entity, typeof(T));
    }

    private static void ValidateEntityForPersistence<T>(T entity) where T : new()
    {
        if (entity is Track { HasPoints: false } track && track.Deleted is null)
        {
            throw new InvalidOperationException("Track must contain at least one point.");
        }
    }
}
