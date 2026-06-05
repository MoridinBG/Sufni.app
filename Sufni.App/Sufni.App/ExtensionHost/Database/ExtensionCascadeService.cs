using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.ExtensionHost.Database;

public sealed class ExtensionCascadeService : IExtensionCascadeService
{
    private readonly Func<CancellationToken, Task<SQLiteAsyncConnection>> getConnectionAsync;
    private readonly IReadOnlyList<ExtensionCascadeRule> rules;
    private readonly Func<IReadOnlyList<IExtensionStateRefreshParticipant>> getRefreshParticipants;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> columnsByTableName;

    public ExtensionCascadeService(
        IExtensionDatabaseConnection databaseConnection,
        IEnumerable<IExtensionDatabaseMigrator> migrators,
        IEnumerable<IExtensionCascadeRuleProvider> ruleProviders,
        IEnumerable<IExtensionStateRefreshParticipant> refreshParticipants)
        : this(
            databaseConnection.GetInitializedConnectionAsync,
            migrators,
            ruleProviders,
            () => refreshParticipants.ToArray())
    {
    }

    internal ExtensionCascadeService(
        SQLiteAsyncConnection connection,
        IEnumerable<IExtensionDatabaseMigrator> migrators,
        IEnumerable<IExtensionCascadeRuleProvider> ruleProviders,
        IEnumerable<IExtensionStateRefreshParticipant> refreshParticipants)
        : this(
            _ => Task.FromResult(connection),
            migrators,
            ruleProviders,
            () => refreshParticipants.ToArray())
    {
    }

    internal ExtensionCascadeService(
        SQLiteAsyncConnection connection,
        IEnumerable<IExtensionDatabaseMigrator> migrators,
        IEnumerable<IExtensionCascadeRuleProvider> ruleProviders,
        Func<IReadOnlyList<IExtensionStateRefreshParticipant>> getRefreshParticipants)
        : this(
            _ => Task.FromResult(connection),
            migrators,
            ruleProviders,
            getRefreshParticipants)
    {
    }

    private ExtensionCascadeService(
        Func<CancellationToken, Task<SQLiteAsyncConnection>> getConnectionAsync,
        IEnumerable<IExtensionDatabaseMigrator> migrators,
        IEnumerable<IExtensionCascadeRuleProvider> ruleProviders,
        Func<IReadOnlyList<IExtensionStateRefreshParticipant>> getRefreshParticipants)
    {
        this.getConnectionAsync = getConnectionAsync;
        rules = ruleProviders.SelectMany(provider => provider.Rules).ToArray();
        this.getRefreshParticipants = getRefreshParticipants;
        columnsByTableName = BuildTableColumnMap(migrators);
    }

    public async Task ApplyForDeletedCoreEntityAsync(
        ExtensionCoreEntityKind kind,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var matchingRules = rules
            .Where(rule => rule.CoreEntityKind == kind)
            .ToArray();
        if (matchingRules.Length == 0)
        {
            return;
        }

        var connection = await getConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var rule in matchingRules)
        {
            ValidateRule(rule);
            await ApplyRuleForDeletedCoreEntityAsync(connection, rule, id, now);
        }

        await RefreshExtensionStateAsync(cancellationToken);
    }

    public async Task RepairOrphansAsync(CancellationToken cancellationToken = default)
        => await RepairOrphansAsync(refreshExtensionState: true, cancellationToken);

    internal async Task RepairOrphansAsync(bool refreshExtensionState, CancellationToken cancellationToken = default)
    {
        if (rules.Count == 0)
        {
            return;
        }

        var connection = await getConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var rule in rules)
        {
            ValidateRule(rule);
            await ApplyOrphanRepairRuleAsync(connection, rule, now);
        }

        if (refreshExtensionState)
        {
            await RefreshExtensionStateAsync(cancellationToken);
        }
    }

    private static async Task ApplyRuleForDeletedCoreEntityAsync(
        SQLiteAsyncConnection connection,
        ExtensionCascadeRule rule,
        Guid id,
        long now)
    {
        var tableName = QuoteIdentifier(rule.TableName);
        var referenceColumnName = QuoteIdentifier(rule.ReferenceColumnName);

        if (rule.Action == ExtensionCascadeAction.HardDelete)
        {
            await connection.ExecuteAsync(
                $"DELETE FROM {tableName} WHERE {referenceColumnName} = ?",
                id);
            return;
        }

        await connection.ExecuteAsync(
            $"""
            UPDATE {tableName}
            SET {QuoteIdentifier(rule.DeletedColumnName)} = ?, {QuoteIdentifier(rule.UpdatedColumnName)} = ?
            WHERE {referenceColumnName} = ? AND {QuoteIdentifier(rule.DeletedColumnName)} IS NULL
            """,
            now,
            now,
            id);
    }

    private static async Task ApplyOrphanRepairRuleAsync(
        SQLiteAsyncConnection connection,
        ExtensionCascadeRule rule,
        long now)
    {
        var tableName = QuoteIdentifier(rule.TableName);
        var referenceColumnName = QuoteIdentifier(rule.ReferenceColumnName);
        var coreTableName = QuoteIdentifier(GetCoreTableName(rule.CoreEntityKind));

        if (rule.Action == ExtensionCascadeAction.HardDelete)
        {
            await connection.ExecuteAsync(
                $"""
                DELETE FROM {tableName}
                WHERE {referenceColumnName} IS NOT NULL
                  AND {referenceColumnName} NOT IN (SELECT id FROM {coreTableName})
                """);
            return;
        }

        await connection.ExecuteAsync(
            $"""
            UPDATE {tableName}
            SET {QuoteIdentifier(rule.DeletedColumnName)} = ?, {QuoteIdentifier(rule.UpdatedColumnName)} = ?
            WHERE {QuoteIdentifier(rule.DeletedColumnName)} IS NULL
              AND {referenceColumnName} IS NOT NULL
              AND {referenceColumnName} NOT IN (SELECT id FROM {coreTableName})
            """,
            now,
            now);
    }

    private void ValidateRule(ExtensionCascadeRule rule)
    {
        if (!columnsByTableName.TryGetValue(rule.TableName, out var columnNames))
        {
            throw new InvalidOperationException(
                $"Extension cascade rule for '{rule.ExtensionId}' references table '{rule.TableName}', but no registered extension migrator declares that table.");
        }

        ValidateColumn(rule, columnNames, rule.ReferenceColumnName);
        if (rule.Action == ExtensionCascadeAction.SoftDelete)
        {
            ValidateColumn(rule, columnNames, rule.DeletedColumnName);
            ValidateColumn(rule, columnNames, rule.UpdatedColumnName);
        }
    }

    private static void ValidateColumn(
        ExtensionCascadeRule rule,
        IReadOnlySet<string> columnNames,
        string columnName)
    {
        if (!columnNames.Contains(columnName))
        {
            throw new InvalidOperationException(
                $"Extension cascade rule for '{rule.ExtensionId}' references column '{columnName}' on table '{rule.TableName}', but the registered extension table does not declare that column.");
        }
    }

    private async Task RefreshExtensionStateAsync(CancellationToken cancellationToken)
    {
        foreach (var participant in getRefreshParticipants())
        {
            await participant.RefreshExtensionStateAsync(cancellationToken);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildTableColumnMap(
        IEnumerable<IExtensionDatabaseMigrator> migrators)
    {
        return migrators
            .SelectMany(migrator => migrator.TableTypes)
            .Distinct()
            .ToDictionary(
                GetTableName,
                GetColumnNames,
                StringComparer.Ordinal);
    }

    private static string GetTableName(Type tableType)
    {
        return tableType.GetCustomAttribute<TableAttribute>()?.Name ?? tableType.Name;
    }

    private static IReadOnlySet<string> GetColumnNames(Type tableType)
    {
        return tableType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GetCoreTableName(ExtensionCoreEntityKind kind) => kind switch
    {
        ExtensionCoreEntityKind.Board => "board",
        ExtensionCoreEntityKind.Bike => "bike",
        ExtensionCoreEntityKind.Setup => "setup",
        ExtensionCoreEntityKind.Session => "session",
        ExtensionCoreEntityKind.Track => "track",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported extension core entity kind.")
    };

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

