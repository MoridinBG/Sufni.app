using System.Collections.Immutable;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.Video.Services;

namespace Sufni.App.Tests.TestSupport.Video;

internal sealed class FakeVideoFilePresence : IVideoFilePresence, IDisposable
{
    private readonly HashSet<string> present = new(StringComparer.Ordinal);
    private readonly Subject<Unit> changed = new();

    public void SetPresent(string path, bool exists)
    {
        if (exists) present.Add(path);
        else present.Remove(path);
        changed.OnNext(Unit.Default);
    }

    public Task<ImmutableDictionary<string, bool>> ReadAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(paths));
    }

    public IObservable<ImmutableDictionary<string, bool>> Observe(IEnumerable<string> paths)
    {
        var captured = paths.ToArray();
        return changed.StartWith(Unit.Default).Select(_ => Read(captured));
    }

    private ImmutableDictionary<string, bool> Read(IEnumerable<string> paths) =>
        paths.Distinct(StringComparer.Ordinal).ToImmutableDictionary(path => path, present.Contains, StringComparer.Ordinal);

    public void Dispose() => changed.Dispose();
}
