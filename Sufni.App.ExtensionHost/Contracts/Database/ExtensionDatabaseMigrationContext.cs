namespace Sufni.App.ExtensionHost.Contracts.Database;

public sealed class ExtensionDatabaseMigrationContext
{
    public ExtensionDatabaseMigrationContext(string extensionId, IExtensionDatabaseSession database)
    {
        ExtensionId = extensionId;
        Database = database;
    }

    public string ExtensionId { get; }
    public IExtensionDatabaseSession Database { get; }
}
