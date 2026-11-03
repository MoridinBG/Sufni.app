namespace Sufni.App.Tests.TestSupport.Io;

public sealed class TempDatabase : IDisposable
{
    private readonly TempDirectory directory;

    public string DirectoryPath => directory.Path;
    public string DatabasePath { get; }

    public TempDatabase(string databaseFileName = "test.db", string prefix = "sufni-db-test")
    {
        directory = new TempDirectory(prefix);
        DatabasePath = Path.Combine(DirectoryPath, databaseFileName);
    }

    public void Dispose() => directory.Dispose();
}
