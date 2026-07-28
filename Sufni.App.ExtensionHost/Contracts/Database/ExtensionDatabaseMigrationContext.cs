namespace Sufni.App.ExtensionHost.Contracts.Database;

public sealed class ExtensionDatabaseMigrationContext
{
    public ExtensionDatabaseMigrationContext(
        string extensionId,
        IExtensionDatabaseTransaction transaction)
    {
        ExtensionId = extensionId;
        Transaction = transaction;
    }

    public string ExtensionId { get; }
    public IExtensionDatabaseTransaction Transaction { get; }
}
