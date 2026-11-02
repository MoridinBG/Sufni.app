using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using Sufni.App.Infrastructure;
using Sufni.App.Video.Models;
using Sufni.App.Video.Services;
using Sufni.App.Video.Views;

namespace Sufni.App.Video.LibVlc;

public sealed class LibVlcVideoPlayerFactory(IUiThreadDispatcher uiThreadDispatcher, IVideoFileAccess fileAccess) : IVideoPlayerFactory
{
    public IVideoPlayer Create()
    {
        try
        {
            return new NativeVideoPlayer(new LibVlcVideoSurface(uiThreadDispatcher), fileAccess, uiThreadDispatcher);
        }
        catch (VLCException e)
        {
            throw new InvalidOperationException("LibVLC could not be initialized.", e);
        }
    }
}

internal sealed class LibVlcVideoSurface : IVideoPlayerSurface
{
    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;
    private readonly VideoView videoView;
    private readonly LibVlcPlaybackBackend backend;
    private Media? currentMedia;
    private NativeVideoSource? currentSource;
    private readonly IUiThreadDispatcher dispatcher;

    public LibVlcVideoSurface(IUiThreadDispatcher uiThreadDispatcher)
    {
        dispatcher = uiThreadDispatcher;
        Core.Initialize();
        libVlc = new LibVLC();
        mediaPlayer = new MediaPlayer(libVlc);
        mediaPlayer.Vout += OnVideoOutput;
        mediaPlayer.EncounteredError += OnNativeError;
        videoView = new VideoView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MediaPlayer = mediaPlayer,
        };
        backend = new LibVlcPlaybackBackend(mediaPlayer, uiThreadDispatcher);
    }

    public Control View => videoView;
    public IVideoPlaybackBackend Backend => backend;
    public event EventHandler<NativeVideoFailureEventArgs>? Failed;
    public event EventHandler<NativeVideoSizeEventArgs>? SizeChanged;

    public async Task LoadAsync(NativeVideoSource source)
    {
        Media? prepared = new Media(libVlc, source.Uri);
        try
        {
            var parsed = await prepared.Parse(MediaParseOptions.ParseLocal, -1, source.CancellationToken);
            source.CancellationToken.ThrowIfCancellationRequested();
            if (parsed != MediaParsedStatus.Done) throw new InvalidDataException($"LibVLC could not parse the video ({parsed}).");
            currentSource = source;
            currentMedia = prepared;
            mediaPlayer.Media = prepared;
            prepared = null;
            TryUpdateVideoSize(source);
        }
        finally
        {
            prepared?.Dispose();
        }
    }

    public void Clear()
    {
        currentSource = null;
        mediaPlayer.Stop();
        mediaPlayer.Media = null;
        currentMedia?.Dispose();
        currentMedia = null;
    }

    private void OnNativeError(object? sender, EventArgs args)
    {
        if (currentSource is { } source)
            Failed?.Invoke(this, new(source, new InvalidOperationException("LibVLC reported a playback failure.")));
    }

    private void OnVideoOutput(object? sender, EventArgs args)
    {
        if (currentSource is { } source) dispatcher.Post(() => TryUpdateVideoSize(source));
    }

    private void TryUpdateVideoSize(NativeVideoSource source)
    {
        if (source.CancellationToken.IsCancellationRequested) return;
        uint width = 0;
        uint height = 0;
        if (mediaPlayer.Size(0, ref width, ref height) && width > 0 && height > 0)
            SizeChanged?.Invoke(this, new(source, width, height));
    }

    public void Dispose()
    {
        mediaPlayer.Vout -= OnVideoOutput;
        mediaPlayer.EncounteredError -= OnNativeError;
        videoView.MediaPlayer = null;
        Clear();
        backend.Dispose();
        mediaPlayer.Dispose();
        libVlc.Dispose();
    }

}

internal sealed class LibVlcPlaybackBackend : IVideoPlaybackBackend, IDisposable
{
    private readonly MediaPlayer mediaPlayer;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    public LibVlcPlaybackBackend(MediaPlayer mediaPlayer, IUiThreadDispatcher uiThreadDispatcher)
    {
        this.mediaPlayer = mediaPlayer;
        this.uiThreadDispatcher = uiThreadDispatcher;
        mediaPlayer.Playing += OnPlaybackStateEvent;
        mediaPlayer.Paused += OnPlaybackStateEvent;
        mediaPlayer.Stopped += OnPlaybackStateEvent;
        mediaPlayer.EndReached += OnPlaybackStateEvent;
        mediaPlayer.TimeChanged += OnTimeChanged;
    }

    public bool IsPlaying => mediaPlayer.IsPlaying;

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler<VideoPositionChangedEventArgs>? PositionChanged;

    public void Play()
    {
        if (mediaPlayer.Media is not null)
        {
            mediaPlayer.Play();
        }
    }

    public void Pause()
    {
        mediaPlayer.Pause();
    }

    public void Seek(TimeSpan position)
    {
        mediaPlayer.Time = Math.Max(0, (long)position.TotalMilliseconds);
    }

    // VLC raises its events on library threads; consumers are UI-bound.
    private void OnPlaybackStateEvent(object? sender, EventArgs e)
    {
        uiThreadDispatcher.Post(() => PlaybackStateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        var position = TimeSpan.FromMilliseconds(e.Time);
        uiThreadDispatcher.Post(() => PositionChanged?.Invoke(this, new VideoPositionChangedEventArgs(position)));
    }

    public void Dispose()
    {
        mediaPlayer.Playing -= OnPlaybackStateEvent;
        mediaPlayer.Paused -= OnPlaybackStateEvent;
        mediaPlayer.Stopped -= OnPlaybackStateEvent;
        mediaPlayer.EndReached -= OnPlaybackStateEvent;
        mediaPlayer.TimeChanged -= OnTimeChanged;
    }
}
