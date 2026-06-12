using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Models;

namespace Sufni.App.Services;

/// <summary>
/// Shared persistence helpers for the SQLite repositories and the merge
/// engine: table-name resolution, existence checks, and validated
/// insert/update entry points.
/// </summary>
internal static class PersistenceGuards
{
    public static string GetTableName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : new()
    {
        return typeof(T).GetCustomAttribute<TableAttribute>()?.Name
               ?? throw new InvalidOperationException($"Type {typeof(T).Name} is missing a SQLite table attribute.");
    }

    public static async Task<bool> EntityExistsAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        Guid id) where T : Synchronizable, new()
    {
        var tableName = GetTableName<T>();
        return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {tableName} WHERE id = ?", id) > 0;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "T is annotated to preserve SQLite-mapped members.")]
    public static Task<int> InsertEntityAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity) where T : new()
    {
        ValidateEntityForPersistence(entity);
        return connection.InsertAsync(entity);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "T is annotated to preserve SQLite-mapped members.")]
    public static Task<int> UpdateEntityAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity) where T : new()
    {
        ValidateEntityForPersistence(entity);
        return connection.UpdateAsync(entity);
    }

    public static void ValidateEntityForPersistence<T>(T entity) where T : new()
    {
        if (entity is Track { HasPoints: false } track && track.Deleted is null)
        {
            throw new InvalidOperationException("Track must contain at least one point.");
        }
    }
}
