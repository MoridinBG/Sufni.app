using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SQLite;
using Sufni.App.Models;

namespace Sufni.App.ExtensionHost.Database;

internal sealed class ExtensionDatabaseTableCatalog
{
    private static readonly IReadOnlySet<string> ReservedTableNames = new HashSet<string>(
        [
            GetTableName(typeof(Board)),
            GetTableName(typeof(Setup)),
            GetTableName(typeof(Bike)),
            GetTableName(typeof(Session)),
            GetTableName(typeof(RecordedSessionSource)),
            GetTableName(typeof(SessionCache)),
            GetTableName(typeof(Synchronization)),
            GetTableName(typeof(PairedDevice)),
            GetTableName(typeof(Track)),
            GetTableName(typeof(ExtensionSchemaVersion)),
        ],
        StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, ExtensionDatabaseTableRegistration> registrationsByTableName;

    private ExtensionDatabaseTableCatalog(
        IReadOnlyDictionary<string, ExtensionDatabaseTableRegistration> registrationsByTableName)
    {
        this.registrationsByTableName = registrationsByTableName;
    }

    public Type[] TableTypes => registrationsByTableName
        .Values
        .Select(registration => registration.TableType)
        .ToArray();

    public bool TryGetRegistration(string tableName, out ExtensionDatabaseTableRegistration registration) =>
        registrationsByTableName.TryGetValue(tableName, out registration!);

    public bool IsDeclaredTableType(Type tableType)
    {
        ArgumentNullException.ThrowIfNull(tableType);

        return registrationsByTableName.TryGetValue(GetTableName(tableType), out var registration) &&
               registration.TableType == tableType;
    }

    public static ExtensionDatabaseTableCatalog Create(IEnumerable<IExtensionDatabaseMigrator> migrators)
    {
        ArgumentNullException.ThrowIfNull(migrators);

        var migratorArray = migrators.ToArray();
        ValidateMigrators(migratorArray);

        var registrations = new Dictionary<string, ExtensionDatabaseTableRegistration>(StringComparer.Ordinal);
        foreach (var migrator in migratorArray)
        {
            foreach (var tableType in migrator.TableTypes)
            {
                var tableName = GetTableName(tableType);
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    throw new InvalidOperationException(
                        $"Extension migrator '{migrator.ExtensionId}' declares table type '{tableType.FullName}' with an empty table name.");
                }

                if (ReservedTableNames.Contains(tableName))
                {
                    throw new InvalidOperationException(
                        $"Extension migrator '{migrator.ExtensionId}' declares table '{tableName}', but that table is reserved by the core database.");
                }

                var registration = new ExtensionDatabaseTableRegistration(
                    migrator.ExtensionId,
                    tableName,
                    tableType,
                    GetColumnNames(tableType));

                if (registrations.TryGetValue(tableName, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Extension table '{tableName}' is declared by both '{existing.ExtensionId}' and '{migrator.ExtensionId}'.");
                }

                registrations.Add(tableName, registration);
            }
        }

        return new ExtensionDatabaseTableCatalog(registrations);
    }

    private static void ValidateMigrators(IReadOnlyList<IExtensionDatabaseMigrator> migrators)
    {
        var extensionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var migrator in migrators)
        {
            ArgumentNullException.ThrowIfNull(migrator);
            ValidateRequiredId(migrator.ExtensionId, "Extension database migrator");

            if (!extensionIds.Add(migrator.ExtensionId))
            {
                throw new InvalidOperationException(
                    $"More than one extension database migrator is registered for extension '{migrator.ExtensionId}'.");
            }

            if (migrator.TargetVersion < 0)
            {
                throw new InvalidOperationException(
                    $"Extension database migrator '{migrator.ExtensionId}' has invalid target version {migrator.TargetVersion}.");
            }

            ArgumentNullException.ThrowIfNull(migrator.TableTypes);
            ArgumentNullException.ThrowIfNull(migrator.Steps);
            ValidateMigrationSteps(migrator);
        }
    }

    private static void ValidateMigrationSteps(IExtensionDatabaseMigrator migrator)
    {
        var targetVersions = new HashSet<int>();
        foreach (var step in migrator.Steps)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (step.TargetVersion <= 0)
            {
                throw new InvalidOperationException(
                    $"Extension database migrator '{migrator.ExtensionId}' declares an invalid migration target version {step.TargetVersion}.");
            }

            if (step.TargetVersion > migrator.TargetVersion)
            {
                throw new InvalidOperationException(
                    $"Extension database migrator '{migrator.ExtensionId}' declares migration step {step.TargetVersion} above target version {migrator.TargetVersion}.");
            }

            if (!targetVersions.Add(step.TargetVersion))
            {
                throw new InvalidOperationException(
                    $"Extension database migrator '{migrator.ExtensionId}' declares duplicate migration step {step.TargetVersion}.");
            }
        }
    }

    private static void ValidateRequiredId(string id, string owner)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"{owner} id is required.");
        }
    }

    private static string GetTableName(Type tableType)
    {
        ArgumentNullException.ThrowIfNull(tableType);
        return tableType.GetCustomAttribute<TableAttribute>()?.Name ?? tableType.Name;
    }

    private static IReadOnlySet<string> GetColumnNames(Type tableType)
    {
        return tableType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }
}

internal sealed record ExtensionDatabaseTableRegistration(
    string ExtensionId,
    string TableName,
    Type TableType,
    IReadOnlySet<string> ColumnNames);
