namespace Sufni.App.ExtensionHost.TestSupport.Io;

public sealed class TempDatabase : IDisposable
{
    public string DirectoryPath { get; }
    public string DatabasePath { get; }

    public TempDatabase(string databaseFileName = "test.db", string prefix = "sufni-db-test")
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        DatabasePath = Path.Combine(DirectoryPath, databaseFileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
