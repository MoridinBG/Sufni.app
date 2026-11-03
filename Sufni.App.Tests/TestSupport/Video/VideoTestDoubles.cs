using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Stores;
using Avalonia.Controls;
using NSubstitute;
using Sufni.App.Sessions.Signals.ViewModels.Editors;

using Sufni.App.Video.Coordinators;
using Sufni.App.Video.Models;
using Sufni.App.Video.Services;
using Sufni.App.Video.Stores;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Tests.TestSupport.Video;

internal sealed class FakeVideoPlaybackBackend : IVideoPlaybackBackend
{
    public bool IsPlaying { get; private set; }
    public List<TimeSpan> SeekedPositions { get; } = [];
    public event EventHandler? PlaybackStateChanged;
    public event EventHandler<VideoPositionChangedEventArgs>? PositionChanged;

    public void Play()
    {
        IsPlaying = true;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        SeekedPositions.Add(position);
    }

    public void RaisePositionChanged(TimeSpan position)
    {
        PositionChanged?.Invoke(this, new VideoPositionChangedEventArgs(position));
    }

    public void SimulateEndReached()
    {
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeVideoPlayer : IVideoPlayer
{
    public Control View { get; } = new Border();
    public FakeVideoPlaybackBackend FakeBackend { get; } = new();
    public IVideoPlaybackBackend Backend => FakeBackend;
    public List<Uri?> LoadedUris { get; } = [];
    public bool IsDisposed { get; private set; }
    public event EventHandler<VideoSizeChangedEventArgs>? VideoSizeChanged;

    public event EventHandler<VideoPlaybackFailedEventArgs>? PlaybackFailed;

    public Task LoadAsync(Uri? videoUri, CancellationToken cancellationToken = default)
    {
        LoadedUris.Add(videoUri);
        return Task.CompletedTask;
    }

    public void RaiseFailure(VideoPlaybackFailure failure) => PlaybackFailed?.Invoke(this, new(failure));

    public void RaiseVideoSizeChanged(double width, double height) =>
        VideoSizeChanged?.Invoke(this, new VideoSizeChangedEventArgs(width, height));

    public void Dispose() => IsDisposed = true;
}

internal sealed class FakeVideoPlayerFactory : IVideoPlayerFactory
{
    public List<FakeVideoPlayer> CreatedPlayers { get; } = [];
    public Exception? CreateException { get; set; }

    public IVideoPlayer Create()
    {
        if (CreateException is not null)
        {
            throw CreateException;
        }

        var player = new FakeVideoPlayer();
        CreatedPlayers.Add(player);
        return player;
    }
}

/// <summary>
/// Builds explicit clip snapshots and panes over a real store without file I/O.
/// </summary>
internal sealed class VideoFixture : IDisposable
{
    private readonly List<SessionVideoStore> stores = [];

    public IVideoCoordinator Coordinator { get; } = Substitute.For<IVideoCoordinator>();
    public FakeVideoPlayerFactory PlayerFactory { get; } = new();

    public SessionVideoSnapshot CreateClip(
        string ownerKey,
        long startTimestamp,
        double durationSeconds,
        long added = 0,
        double alignmentOffsetSeconds = 0,
        bool fileExists = true)
    {
        var id = Guid.NewGuid();
        return new SessionVideoSnapshot(id, ownerKey, $"/videos/{id:N}.mp4", startTimestamp,
            durationSeconds, alignmentOffsetSeconds, added, fileExists);
    }

    public static SessionVideoStore CreateEmptyStore() =>
        new(new StorePublication(new InlineUiThreadDispatcher()));

    public async Task<SessionVideoStore> CreateStoreAsync(params SessionVideoSnapshot[] clips)
    {
        var store = new SessionVideoStore(new StorePublication(new InlineUiThreadDispatcher()));
        stores.Add(store);
        await store.ReplaceAllAsync(clips);
        return store;
    }

    public async Task<VideoPaneViewModel> CreatePaneAsync(
        IPlotTimeline timeline,
        IObservable<VideoSessionTiming> sessionTiming,
        params SessionVideoSnapshot[] clips)
    {
        var store = await CreateStoreAsync(clips);
        return new VideoPaneViewModel(timeline, sessionTiming, store, Coordinator, PlayerFactory);
    }

    public void Dispose()
    {
        foreach (var store in stores) store.Dispose();
    }
}
