using Sufni.App.ExtensionHost.Database;

namespace Sufni.App.Tests.Infrastructure;

internal sealed class TestExtensionMigrator(
    string extensionId,
    int targetVersion,
    IReadOnlyList<Type> tableTypes,
    IReadOnlyList<ExtensionDatabaseMigrationStep> steps) : IExtensionDatabaseMigrator
{
    public string ExtensionId { get; } = extensionId;
    public int TargetVersion { get; } = targetVersion;
    public IReadOnlyList<Type> TableTypes { get; } = tableTypes;
    public IReadOnlyList<ExtensionDatabaseMigrationStep> Steps { get; } = steps;
}
