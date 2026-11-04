using System.Reactive.Linq;
using DynamicData;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Video;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Services;
using Sufni.App.Video.Coordinators;
using Sufni.App.Video.Models;
using Sufni.App.Video.Services;
using Sufni.App.Video.Stores;
namespace Sufni.App.Tests.Video.Coordinators;

public class VideoCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset VideoStart = new(2026, 6, 3, 14, 0, 0, TimeSpan.Zero);

    private readonly IVideoImportFileService fileService = Substitute.For<IVideoImportFileService>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionVideoRepository videoRepository = Substitute.For<ISessionVideoRepository>();
    private readonly ISessionVideoStoreWriter videoStore = Substitute.For<ISessionVideoStoreWriter>();
    private readonly IRecordedSessionSourceStore sourceStore = Substitute.For<IRecordedSessionSourceStore>();
    private readonly VideoCoordinator coordinator;
    private readonly FakeVideoFilePresence presence = new();
    private readonly FakeTimeProvider time = new(VideoStart.AddDays(1));

    public VideoCoordinatorTests()
    {
        videoStore.Connect().Returns(Observable.Empty<IChangeSet<SessionVideoSnapshot, Guid>>());
        coordinator = new VideoCoordinator(
            fileService,
            sessionRepository,
            videoRepository,
            videoStore,
            new InlineBackgroundTaskRunner(),
            time, sourceStore, presence, new InlineUiThreadDispatcher());
        videoRepository
            .LinkAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<double>(), Arg.Any<long>(), Arg.Any<IReadOnlyCollection<SessionVideoOwner>>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<SessionVideoOwner>>(4)
                .DistinctBy(owner => owner.OwnerKey)
                .Select(owner => SessionVideoSnapshot.From(new SessionVideoClip { Id = Guid.NewGuid(), OwnerKey = owner.OwnerKey }, true))
                .ToList());
    }

    [Fact]
    public async Task StopAsync_WaitsForAlignmentPersistenceAndPublication()
    {
        using var store = new SessionVideoStore(new StorePublication(new InlineUiThreadDispatcher()));
        var clip = new SessionVideoSnapshot(Guid.NewGuid(), "source", "/videos/ride.mp4", 0, 60, 2, 1, true);
        await store.PublishClipsAsync([clip]);
        var write = new TaskCompletionSource<SessionVideoSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        videoRepository.UpdateAlignmentAsync(clip.Id, 3.5).Returns(write.Task);
        using var owner = new VideoCoordinator(fileService, sessionRepository, videoRepository,
            store, new InlineBackgroundTaskRunner(), time, sourceStore, presence, new InlineUiThreadDispatcher());
        var operation = owner.ShiftAlignmentAsync(clip.Id, 1.5);

        var stop = owner.StopAsync();
        Assert.False(stop.IsCompleted);
        Assert.Equal(2, store.Get(clip.Id)!.AlignmentOffsetSeconds);
        write.SetResult(clip with { AlignmentOffsetSeconds = 3.5 });
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3.5, store.Get(clip.Id)!.AlignmentOffsetSeconds);
        Assert.IsType<VideoAlignmentResult.Updated>(await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PresenceChange_PublishesAllClipsForThePathTogether()
    {
        var dispatcher = new InlineUiThreadDispatcher();
        var publication = new StorePublication(dispatcher);
        using var store = new SessionVideoStore(publication);
        var first = new SessionVideoSnapshot(Guid.NewGuid(), "first", "/videos/shared.mp4", 0, 60, 0, 1, false);
        var second = first with { Id = Guid.NewGuid(), OwnerKey = "second" };
        await store.ReplaceAllAsync([first, second]);
        using var owner = new VideoCoordinator(fileService, sessionRepository, videoRepository,
            store, new InlineBackgroundTaskRunner(), time, sourceStore, presence, dispatcher);
        var published = new TaskCompletionSource<IReadOnlyList<SessionVideoSnapshot>>();
        using var subscription = publication.Committed.Subscribe(_ => published.TrySetResult(store.GetAll()));

        presence.SetPresent(first.Path, true);
        var values = await published.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, values.Count);
        Assert.All(values, clip => Assert.True(clip.FileExists));
        Assert.Equal(first.OwnerKey, store.Get(first.Id)!.OwnerKey);
    }

    [Fact]
    public async Task Dispose_RejectsQueuedPresencePublication()
    {
        var dispatcher = new ManualUiThreadDispatcher();
        using var store = new SessionVideoStore(new StorePublication(new InlineUiThreadDispatcher()));
        var clip = new SessionVideoSnapshot(Guid.NewGuid(), "source", "/videos/queued.mp4", 0, 60, 0, 1, false);
        await store.ReplaceAllAsync([clip]);
        var owner = new VideoCoordinator(fileService, sessionRepository, videoRepository,
            store, new InlineBackgroundTaskRunner(), time, sourceStore, presence, dispatcher);
        presence.SetPresent(clip.Path, true);

        owner.Dispose();
        await dispatcher.DrainAsync();

        Assert.False(store.Get(clip.Id)!.FileExists);
    }

    [Fact]
    public async Task ImportAsync_WaitsForAllMatchingSourceIdentities_AndAcceptsRetryAfterArrival()
    {
        SelectVideo(durationSeconds: 60);
        var available = CreateSession(VideoStart, 60);
        var waiting = CreateSession(VideoStart, 60);
        waiting.SourceSessionId = Guid.NewGuid();
        sessionRepository.GetSessionsAsync().Returns([available, waiting]);

        Assert.IsType<VideoImportResult.SourceUnavailable>(await coordinator.ImportAsync(TestContext.Current.CancellationToken));
        await AssertNothingLinkedAsync();
        sourceStore.Get(waiting.SourceSessionId.Value).Returns(new RecordedSessionSourceSnapshot(
            waiting.SourceSessionId.Value, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, "other-hash"));

        Assert.Equal(new VideoImportResult.Linked(2), await coordinator.ImportAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_LinksVideoToEverySessionItOverlaps()
    {
        SelectVideo(durationSeconds: 600);
        var inside = CreateSession(VideoStart.AddMinutes(2), durationSeconds: 60);
        var overlappingStart = CreateSession(VideoStart.AddMinutes(-1), durationSeconds: 120);
        var before = CreateSession(VideoStart.AddMinutes(-10), durationSeconds: 60);
        sessionRepository.GetSessionsAsync().Returns([inside, overlappingStart, before]);

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new VideoImportResult.Linked(2), result);
        await videoRepository.Received(1).LinkAsync(
            Arg.Any<string>(),
            VideoStart.ToUnixTimeSeconds(),
            600,
            VideoStart.AddDays(1).ToUnixTimeMilliseconds(),
            Arg.Is<IReadOnlyCollection<SessionVideoOwner>>(owners => owners.SequenceEqual(new[]
            {
                new SessionVideoOwner(sourceStore.Get(inside.Id)!.SourceHash, inside.Timestamp!.Value),
                new SessionVideoOwner(sourceStore.Get(overlappingStart.Id)!.SourceHash, overlappingStart.Timestamp!.Value)
            })));
        await videoStore.Received(1).PublishClipsAsync(Arg.Is<IReadOnlyCollection<SessionVideoSnapshot>>(clips => clips.Count == 2));
    }

    [Fact]
    public async Task ImportAsync_ReturnsCancelled_WhenNoFileIsSelected()
    {
        fileService.ReadAsync(Arg.Any<CancellationToken>()).Returns(new VideoFileReadResult.Cancelled());

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.IsType<VideoImportResult.Cancelled>(result);
        await AssertNothingLinkedAsync();
    }

    [Fact]
    public async Task ImportAsync_Fails_WhenMetadataIsInvalid()
    {
        fileService.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new VideoFileReadResult.Failed(VideoImportFailureReason.InvalidMetadata, "no movie header"));

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(VideoImportFailureReason.InvalidMetadata, Assert.IsType<VideoImportResult.Failed>(result).Reason);
        await AssertNothingLinkedAsync();
    }

    [Fact]
    public async Task ImportAsync_ReturnsNoMatchingSession_WhenNoSessionOverlapsVideo()
    {
        SelectVideo(durationSeconds: 60);
        var session = CreateSession(VideoStart.AddHours(1), durationSeconds: 60);
        sessionRepository.GetSessionsAsync().Returns([session]);

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.IsType<VideoImportResult.NoMatchingSession>(result);
        await AssertNothingLinkedAsync();
    }

    [Fact]
    public async Task ShiftAlignmentAsync_AddsDeltaToCurrentOffset_AndPublishesClip()
    {
        using var store = new SessionVideoStore(new StorePublication(new InlineUiThreadDispatcher()));
        var clip = new SessionVideoSnapshot(Guid.NewGuid(), "source", "/videos/ride.mp4", 0, 60, 2, 1, true);
        await store.PublishClipsAsync([clip]);
        videoRepository.UpdateAlignmentAsync(clip.Id, Arg.Any<double>())
            .Returns(call => clip with { AlignmentOffsetSeconds = call.ArgAt<double>(1) });
        using var owner = new VideoCoordinator(fileService, sessionRepository, videoRepository,
            store, new InlineBackgroundTaskRunner(), time, sourceStore, presence, new InlineUiThreadDispatcher());

        var shifted = await owner.ShiftAlignmentAsync(clip.Id, 1.5);

        Assert.IsType<VideoAlignmentResult.Updated>(shifted);
        Assert.Equal(3.5, store.Get(clip.Id)!.AlignmentOffsetSeconds);
    }

    [Fact]
    public async Task ShiftAlignmentAsync_ReturnsClipMissing_WhenClipIsGone()
    {
        var clipId = Guid.NewGuid();
        videoStore.Get(clipId).Returns(SessionVideoSnapshot.From(new SessionVideoClip { Id = clipId }, true));
        videoRepository.UpdateAlignmentAsync(clipId, Arg.Any<double>()).Returns((SessionVideoSnapshot?)null);

        var shifted = await coordinator.ShiftAlignmentAsync(clipId, 1);

        Assert.IsType<VideoAlignmentResult.ClipMissing>(shifted);
        await videoStore.DidNotReceive().PublishClipsAsync(Arg.Any<IReadOnlyCollection<SessionVideoSnapshot>>());
    }

    [Fact]
    public async Task ImportAsync_ReturnsFailure_WhenPersistenceFails()
    {
        SelectVideo(durationSeconds: 60);
        var session = CreateSession(VideoStart, durationSeconds: 60);
        sessionRepository.GetSessionsAsync().Returns([session]);
        videoRepository.LinkAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<double>(), Arg.Any<long>(),
            Arg.Any<IReadOnlyCollection<SessionVideoOwner>>()).Returns(Task.FromException<IReadOnlyList<SessionVideoSnapshot>>(
                new IOException("database unavailable")));

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(VideoImportFailureReason.Import, Assert.IsType<VideoImportResult.Failed>(result).Reason);
        await videoStore.DidNotReceive().PublishClipsAsync(Arg.Any<IReadOnlyCollection<SessionVideoSnapshot>>());
    }

    [Fact]
    public async Task ShiftAlignmentAsync_ReturnsFailure_WhenPersistenceFails()
    {
        var clipId = Guid.NewGuid();
        videoStore.Get(clipId).Returns(SessionVideoSnapshot.From(new SessionVideoClip { Id = clipId }, true));
        videoRepository.UpdateAlignmentAsync(clipId, Arg.Any<double>())
            .Returns(Task.FromException<SessionVideoSnapshot?>(new IOException("database unavailable")));

        var result = await coordinator.ShiftAlignmentAsync(clipId, 1);

        Assert.Equal(VideoAlignmentFailureReason.Save, Assert.IsType<VideoAlignmentResult.Failed>(result).Reason);
        await videoStore.DidNotReceive().PublishClipsAsync(Arg.Any<IReadOnlyCollection<SessionVideoSnapshot>>());
    }

    public void Dispose()
    {
        coordinator.Dispose();
        presence.Dispose();
    }

    private void SelectVideo(double durationSeconds)
    {
        fileService.ReadAsync(Arg.Any<CancellationToken>()).Returns(new VideoFileReadResult.Read(
            "ride.mp4", "/videos/ride.mp4", new VideoMetadata(VideoStart, TimeSpan.FromSeconds(durationSeconds))));
    }

    private Session CreateSession(DateTimeOffset start, double durationSeconds)
    {
        var session = new Session(Guid.NewGuid(), "ride", string.Empty, setup: null, start.ToUnixTimeSeconds())
        {
            DurationSeconds = durationSeconds,
        };
        sourceStore.Get(session.Id).Returns(new RecordedSessionSourceSnapshot(
            session.Id, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, Guid.NewGuid().ToString("N")));
        return session;
    }

    private async Task AssertNothingLinkedAsync()
    {
        await videoRepository.DidNotReceive().LinkAsync(
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<double>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyCollection<SessionVideoOwner>>());
    }
}
