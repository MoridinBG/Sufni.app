using Avalonia.Controls;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

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

    public void Load(Uri? videoUri) => LoadedUris.Add(videoUri);

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
/// Builds video clips backed by real files, so the store observes them as
/// present, and video panes over a real store.
/// </summary>
internal sealed class VideoFixture : IDisposable
{
    private readonly TempDirectory directory = new("sufni-video-test");

    public ISessionVideoRepository Repository { get; } = Substitute.For<ISessionVideoRepository>();
    public IVideoCoordinator Coordinator { get; } = Substitute.For<IVideoCoordinator>();
    public FakeVideoPlayerFactory PlayerFactory { get; } = new();

    public SessionVideoClip CreateClip(
        Guid sessionId,
        long startTimestamp,
        double durationSeconds,
        long added = 0,
        double alignmentOffsetSeconds = 0,
        bool fileExists = true)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(directory.Path, $"{id:N}.mp4");
        if (fileExists)
        {
            File.WriteAllBytes(path, []);
        }

        return new SessionVideoClip
        {
            Id = id,
            SessionId = sessionId,
            Path = path,
            StartTimestamp = startTimestamp,
            DurationSeconds = durationSeconds,
            AlignmentOffsetSeconds = alignmentOffsetSeconds,
            Added = added,
        };
    }

    public static SessionVideoStore CreateEmptyStore() =>
        new(Substitute.For<ISessionVideoRepository>(), new InlineUiThreadDispatcher());

    public async Task<SessionVideoStore> CreateStoreAsync(params SessionVideoClip[] clips)
    {
        Repository.GetLiveAsync().Returns([.. clips]);
        var store = new SessionVideoStore(Repository, new InlineUiThreadDispatcher());
        await store.RefreshAsync(TestContext.Current.CancellationToken);
        return store;
    }

    public async Task<VideoPaneViewModel> CreatePaneAsync(
        Guid sessionId,
        IRecordedSessionTimeline timeline,
        IObservable<VideoSessionTiming> sessionTiming,
        params SessionVideoClip[] clips)
    {
        var store = await CreateStoreAsync(clips);
        return new VideoPaneViewModel(sessionId, timeline, sessionTiming, store, Coordinator, PlayerFactory);
    }

    public void Dispose() => directory.Dispose();
}
