namespace Sufni.App.Tests.TestSupport.Async;

public sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly Lock gate = new();
    private readonly List<T> reports = [];

    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (gate) return reports.ToArray();
        }
    }

    public void Report(T value)
    {
        lock (gate) reports.Add(value);
    }
}
