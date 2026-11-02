using System.Reactive.Disposables;
using NSubstitute;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.Models;
using Sufni.App.Video.Services;
using Sufni.App.Video.Views;

namespace Sufni.App.Tests.Video.Views;

public class NativeVideoPlayerTests
{
    private readonly IVideoPlayerSurface surface = Substitute.For<IVideoPlayerSurface>();
    private readonly IVideoFileAccess access = Substitute.For<IVideoFileAccess>();
    private NativeVideoPlayer Create() => new(surface, access, new InlineUiThreadDispatcher());

    [Fact]
    public async Task UnreadableFile_ReportsOpenFailureWithoutStartingNativePlayback()
    {
        using var player = Create();
        VideoPlaybackFailure? failure = null;
        player.PlaybackFailed += (_, args) => failure = args.Failure;

        await player.LoadAsync(new Uri("file:///videos/missing.mov"), TestContext.Current.CancellationToken);

        Assert.Equal(VideoPlaybackFailureReason.Open, failure?.Reason);
        await surface.DidNotReceive().LoadAsync(Arg.Any<NativeVideoSource>());
    }

    [Fact]
    public async Task NativeLoadFailure_ReportsTypedFailureAndReleasesAccess()
    {
        var released = false;
        access.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new VideoFileAccessLease("/videos/ride.mov", Disposable.Create(() => released = true)));
        surface.LoadAsync(Arg.Any<NativeVideoSource>()).Returns(Task.FromException(new InvalidDataException("unreadable track")));
        using var player = Create();
        var failures = new List<VideoPlaybackFailure>();
        player.PlaybackFailed += (_, args) => failures.Add(args.Failure);

        await player.LoadAsync(new Uri("file:///videos/ride.mov"), TestContext.Current.CancellationToken);

        Assert.Equal(VideoPlaybackFailureReason.Open, Assert.Single(failures).Reason);
        Assert.True(released);
    }

    [Fact]
    public async Task RuntimeFailure_IsReportedOnceAndReleasesAccess()
    {
        var released = false;
        NativeVideoSource? source = null;
        access.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new VideoFileAccessLease("/videos/resolved.mov", Disposable.Create(() => released = true)));
        surface.LoadAsync(Arg.Any<NativeVideoSource>()).Returns(call => { source = call.Arg<NativeVideoSource>(); return Task.CompletedTask; });
        using var player = Create();
        var failures = new List<VideoPlaybackFailure>();
        player.PlaybackFailed += (_, args) => failures.Add(args.Failure);
        await player.LoadAsync(new Uri("file:///videos/original.mov"), TestContext.Current.CancellationToken);
        Assert.Equal("/videos/resolved.mov", source!.Uri.LocalPath);
        Assert.False(released);

        var error = new NativeVideoFailureEventArgs(source, new InvalidOperationException("native failure"));
        surface.Failed += Raise.Event<EventHandler<NativeVideoFailureEventArgs>>(surface, error);
        surface.Failed += Raise.Event<EventHandler<NativeVideoFailureEventArgs>>(surface, error);

        Assert.Equal(VideoPlaybackFailureReason.Playback, Assert.Single(failures).Reason);
        Assert.True(released);
    }

    [Fact]
    public async Task ReplacedLoad_CannotReportFailureOrRetainItsAccess()
    {
        var first = new TaskCompletionSource<VideoFileAccessLease?>();
        var firstReleased = false;
        var secondReleased = false;
        access.AcquireAsync("/videos/first.mov", Arg.Any<CancellationToken>()).Returns(first.Task);
        access.AcquireAsync("/videos/second.mov", Arg.Any<CancellationToken>()).Returns(
            new VideoFileAccessLease("/videos/second.mov", Disposable.Create(() => secondReleased = true)));
        using var player = Create();
        var failures = new List<VideoPlaybackFailure>();
        player.PlaybackFailed += (_, args) => failures.Add(args.Failure);
        var loading = player.LoadAsync(new Uri("file:///videos/first.mov"), TestContext.Current.CancellationToken);
        await player.LoadAsync(new Uri("file:///videos/second.mov"), TestContext.Current.CancellationToken);
        first.SetResult(new VideoFileAccessLease("/videos/first.mov", Disposable.Create(() => firstReleased = true)));
        await loading.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.True(firstReleased);
        Assert.False(secondReleased);
        Assert.Empty(failures);
        await surface.Received(1).LoadAsync(Arg.Is<NativeVideoSource>(value => value.Uri.LocalPath == "/videos/second.mov"));
        player.Dispose();
        Assert.True(secondReleased);
    }
}
