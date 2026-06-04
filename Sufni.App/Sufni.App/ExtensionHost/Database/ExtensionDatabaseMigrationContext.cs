using SQLite;

namespace Sufni.App.ExtensionHost.Database;

public sealed class ExtensionDatabaseMigrationContext
{
    public ExtensionDatabaseMigrationContext(string extensionId, SQLiteAsyncConnection connection)
    {
        ExtensionId = extensionId;
        Connection = connection;
    }

    public string ExtensionId { get; }
    public SQLiteAsyncConnection Connection { get; }
}

