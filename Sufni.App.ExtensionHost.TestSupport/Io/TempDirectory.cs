namespace Sufni.App.ExtensionHost.TestSupport.Io;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string prefix = "sufni-dir-test")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
