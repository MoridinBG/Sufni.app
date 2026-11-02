using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.App.Video.Models;
using Sufni.App.Video.Services;

namespace Sufni.App.Video.Views;

/// <summary>Owns native source loading, the file-access lease and typed failure reporting.</summary>
public sealed class NativeVideoPlayer : IVideoPlayer
{
    private static readonly ILogger logger = Log.ForContext<NativeVideoPlayer>();
    private readonly IVideoPlayerSurface surface;
    private readonly IVideoFileAccess fileAccess;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly CancellableOperation loadOperation = new();
    private VideoFileAccessLease? access;
    private bool disposed;

    public NativeVideoPlayer(IVideoPlayerSurface surface, IVideoFileAccess fileAccess, IUiThreadDispatcher dispatcher)
    {
        this.surface = surface;
        this.fileAccess = fileAccess;
        this.dispatcher = dispatcher;
        surface.Failed += OnFailed;
        surface.SizeChanged += OnSizeChanged;
    }

    public Control View => surface.View;
    public IVideoPlaybackBackend Backend => surface.Backend;
    public event EventHandler<VideoSizeChangedEventArgs>? VideoSizeChanged;
    public event EventHandler<VideoPlaybackFailedEventArgs>? PlaybackFailed;

    public async Task LoadAsync(Uri? videoUri, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var token = loadOperation.Start(cancellationToken);
        await dispatcher.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;
            surface.Clear();
            access?.Dispose();
            access = null;
        });
        if (videoUri is null || token.IsCancellationRequested) return;
        var source = new NativeVideoSource(videoUri, token);
        VideoFileAccessLease? acquired = null;
        try
        {
            acquired = await fileAccess.AcquireAsync(videoUri.LocalPath, token);
            if (token.IsCancellationRequested) return;
            if (acquired is null)
            {
                await dispatcher.InvokeAsync(() => ReportFailure(source, VideoPlaybackFailureReason.Open, null));
                return;
            }
            var prepared = acquired;
            acquired = null;
            await dispatcher.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested)
                {
                    prepared.Dispose();
                    return;
                }
                access = prepared;
                await surface.LoadAsync(source with { Uri = new Uri(prepared.Path) });
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await dispatcher.InvokeAsync(() => ReportFailure(source, VideoPlaybackFailureReason.Open, exception));
        }
        finally
        {
            acquired?.Dispose();
        }
    }

    private void OnFailed(object? sender, NativeVideoFailureEventArgs args) =>
        dispatcher.Post(() => ReportFailure(args.Source, VideoPlaybackFailureReason.Playback, args.Exception));

    private void ReportFailure(NativeVideoSource source, VideoPlaybackFailureReason reason, Exception? exception)
    {
        if (source.CancellationToken.IsCancellationRequested) return;
        loadOperation.Cancel();
        surface.Clear();
        access?.Dispose();
        access = null;
        if (exception is not null) logger.Error(exception, "Video playback failed for {VideoUri}", source.Uri);
        PlaybackFailed?.Invoke(this, new(new(reason, exception?.Message)));
    }

    private void OnSizeChanged(object? sender, NativeVideoSizeEventArgs args) => dispatcher.Post(() =>
    {
        if (!args.Source.CancellationToken.IsCancellationRequested)
            VideoSizeChanged?.Invoke(this, new(args.Width, args.Height));
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        loadOperation.Dispose();
        surface.Failed -= OnFailed;
        surface.SizeChanged -= OnSizeChanged;
        surface.Clear();
        access?.Dispose();
        access = null;
        surface.Dispose();
    }
}
