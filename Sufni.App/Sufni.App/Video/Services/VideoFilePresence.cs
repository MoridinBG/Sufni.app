using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;

namespace Sufni.App.Video.Services;

/// <summary>Reads and observes availability of linked video files without blocking presentation.</summary>
public interface IVideoFilePresence
{
    Task<ImmutableDictionary<string, bool>> ReadAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    IObservable<ImmutableDictionary<string, bool>> Observe(IEnumerable<string> paths);
}

internal sealed class VideoFilePresence(IBackgroundTaskRunner background, TimeProvider clock) : IVideoFilePresence
{
    public Task<ImmutableDictionary<string, bool>> ReadAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var captured = paths.Distinct(StringComparer.Ordinal).ToImmutableArray();
        return background.RunAsync(() => captured.ToImmutableDictionary(path => path, File.Exists, StringComparer.Ordinal), cancellationToken);
    }

    public IObservable<ImmutableDictionary<string, bool>> Observe(IEnumerable<string> paths)
    {
        var captured = paths.Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (captured.IsEmpty) return Observable.Empty<ImmutableDictionary<string, bool>>();
        return Observable.Create<ImmutableDictionary<string, bool>>(async (observer, token) =>
        {
            ImmutableDictionary<string, bool>? previous = null;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var current = await ReadAsync(captured, token);
                    token.ThrowIfCancellationRequested();
                    if (previous is null || current.Any(item => previous[item.Key] != item.Value))
                        observer.OnNext(current);
                    previous = current;
                    await Task.Delay(TimeSpan.FromSeconds(5), clock, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        });
    }
}
