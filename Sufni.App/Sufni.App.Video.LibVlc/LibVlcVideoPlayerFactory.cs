using System;
using Avalonia.Controls;
using Avalonia.Layout;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Video.Models;

namespace Sufni.App.Video.LibVlc;

public sealed class LibVlcVideoPlayerFactory(IUiThreadDispatcher uiThreadDispatcher) : IVideoPlayerFactory
{
    public IVideoPlayer Create()
    {
        try
        {
            return new LibVlcVideoPlayer(uiThreadDispatcher);
        }
        catch (VLCException e)
        {
            throw new InvalidOperationException("LibVLC could not be initialized.", e);
        }
    }
}

internal sealed class LibVlcVideoPlayer : IVideoPlayer
{
    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;
    private readonly VideoView videoView;
    private readonly LibVlcPlaybackBackend backend;
    private Media? currentMedia;

    public LibVlcVideoPlayer(IUiThreadDispatcher uiThreadDispatcher)
    {
        Core.Initialize();
        libVlc = new LibVLC();
        mediaPlayer = new MediaPlayer(libVlc);
        mediaPlayer.Vout += (_, _) => uiThreadDispatcher.Post(TryUpdateVideoSize);
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
    public event EventHandler<VideoSizeChangedEventArgs>? VideoSizeChanged;

    public void Load(Uri? videoUri)
    {
        mediaPlayer.Stop();
        currentMedia?.Dispose();
        currentMedia = null;

        if (videoUri is null)
        {
            mediaPlayer.Media = null;
            return;
        }

        currentMedia = new Media(libVlc, videoUri);
        mediaPlayer.Media = currentMedia;
        TryUpdateVideoSize();
    }

    private void TryUpdateVideoSize()
    {
        uint width = 0;
        uint height = 0;
        if (mediaPlayer.Size(0, ref width, ref height) && width > 0 && height > 0)
        {
            VideoSizeChanged?.Invoke(this, new VideoSizeChangedEventArgs(width, height));
        }
    }

    public void Dispose()
    {
        videoView.MediaPlayer = null;
        mediaPlayer.Stop();
        currentMedia?.Dispose();
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
