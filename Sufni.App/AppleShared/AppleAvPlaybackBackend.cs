using System;
using AVFoundation;
using CoreFoundation;
using CoreMedia;
using Foundation;
using Sufni.App.Infrastructure;
using Sufni.App.Video.Models;

namespace Sufni.App.AppleShared;

internal sealed class AppleAvPlaybackBackend : IVideoPlaybackBackend, IDisposable
{
    private const int SeekTimescale = 1_000_000;
    private static readonly CMTime ZeroTolerance = new(0, 1);
    private static readonly CMTime PositionObservationInterval = new(1, 10);

    private readonly AVPlayer player;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly NSObject periodicTimeObserver;
    private readonly IDisposable rateObserver;

    public AppleAvPlaybackBackend(AVPlayer player, IUiThreadDispatcher uiThreadDispatcher)
    {
        this.player = player;
        this.uiThreadDispatcher = uiThreadDispatcher;
        periodicTimeObserver = player.AddPeriodicTimeObserver(
            PositionObservationInterval,
            DispatchQueue.MainQueue,
            OnPeriodicTime);
        rateObserver = player.AddObserver(
            "rate",
            NSKeyValueObservingOptions.New,
            _ => uiThreadDispatcher.Post(() => PlaybackStateChanged?.Invoke(this, EventArgs.Empty)));
    }

    public bool IsPlaying => player.Rate > 0;

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler<VideoPositionChangedEventArgs>? PositionChanged;

    public void Play()
    {
        player.Play();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        player.Pause();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        var clampedSeconds = Math.Max(0, position.TotalSeconds);
        var seekUnits = (long)Math.Round(clampedSeconds * SeekTimescale, MidpointRounding.AwayFromZero);
        var targetTime = new CMTime(seekUnits, SeekTimescale);

        player.CurrentItem?.CancelPendingSeeks();
        player.Seek(targetTime, ZeroTolerance, ZeroTolerance);
    }

    private void OnPeriodicTime(CMTime time)
    {
        if (time.IsInvalid || !double.IsFinite(time.Seconds))
        {
            return;
        }

        var position = TimeSpan.FromSeconds(time.Seconds);
        uiThreadDispatcher.Post(() => PositionChanged?.Invoke(this, new VideoPositionChangedEventArgs(position)));
    }

    public void Dispose()
    {
        player.RemoveTimeObserver(periodicTimeObserver);
        periodicTimeObserver.Dispose();
        rateObserver.Dispose();
    }
}
