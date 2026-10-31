using Avalonia.Platform.Storage;
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

public class VideoCoordinatorTests
{
    private static readonly DateTimeOffset VideoStart = new(2026, 6, 3, 14, 0, 0, TimeSpan.Zero);

    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IVideoMetadataReader metadataReader = Substitute.For<IVideoMetadataReader>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionVideoRepository videoRepository = Substitute.For<ISessionVideoRepository>();
    private readonly ISessionVideoStoreWriter videoStore = Substitute.For<ISessionVideoStoreWriter>();
    private readonly IRecordedSessionSourceStore sourceStore = Substitute.For<IRecordedSessionSourceStore>();
    private readonly VideoCoordinator coordinator;
    private readonly FakeTimeProvider time = new(VideoStart.AddDays(1));

    public VideoCoordinatorTests()
    {
        coordinator = new VideoCoordinator(
            filesService,
            metadataReader,
            sessionRepository,
            videoRepository,
            videoStore,
            new InlineBackgroundTaskRunner(),
            time, sourceStore);
        videoRepository
            .LinkAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<double>(), Arg.Any<long>(), Arg.Any<IReadOnlyCollection<SessionVideoOwner>>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<SessionVideoOwner>>(4)
                .DistinctBy(owner => owner.OwnerKey)
                .Select(owner => new SessionVideoClip { Id = Guid.NewGuid(), OwnerKey = owner.OwnerKey })
                .ToList());
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
        filesService.OpenVideoFileAsync().Returns((IStorageFile?)null);

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.IsType<VideoImportResult.Cancelled>(result);
        await AssertNothingLinkedAsync();
    }

    [Fact]
    public async Task ImportAsync_Fails_WhenMetadataIsInvalid()
    {
        SelectFile();
        metadataReader.ReadAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new VideoMetadataReadResult.Invalid("no movie header"));

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
        var clip = new SessionVideoClip { Id = Guid.NewGuid(), AlignmentOffsetSeconds = 3.5 };
        videoStore.Get(clip.Id).Returns(SessionVideoSnapshot.From(new SessionVideoClip { Id = clip.Id, AlignmentOffsetSeconds = 2 }));
        videoRepository.UpdateAlignmentAsync(clip.Id, 3.5).Returns(clip);

        var shifted = await coordinator.ShiftAlignmentAsync(clip.Id, 1.5);

        Assert.IsType<VideoAlignmentResult.Updated>(shifted);
        await videoStore.Received(1).PublishClipsAsync(Arg.Is<IReadOnlyCollection<SessionVideoSnapshot>>(clips => clips.Single().Id == clip.Id && clips.Single().AlignmentOffsetSeconds == clip.AlignmentOffsetSeconds));
    }

    [Fact]
    public async Task ShiftAlignmentAsync_ReturnsClipMissing_WhenClipIsGone()
    {
        var clipId = Guid.NewGuid();
        videoStore.Get(clipId).Returns(SessionVideoSnapshot.From(new SessionVideoClip { Id = clipId }));
        videoRepository.UpdateAlignmentAsync(clipId, Arg.Any<double>()).Returns((SessionVideoClip?)null);

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
            Arg.Any<IReadOnlyCollection<SessionVideoOwner>>()).Returns(Task.FromException<IReadOnlyList<SessionVideoClip>>(
                new IOException("database unavailable")));

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(VideoImportFailureReason.Import, Assert.IsType<VideoImportResult.Failed>(result).Reason);
        await videoStore.DidNotReceive().PublishClipsAsync(Arg.Any<IReadOnlyCollection<SessionVideoSnapshot>>());
    }

    [Fact]
    public async Task ShiftAlignmentAsync_ReturnsFailure_WhenPersistenceFails()
    {
        var clipId = Guid.NewGuid();
        videoStore.Get(clipId).Returns(SessionVideoSnapshot.From(new SessionVideoClip { Id = clipId }));
        videoRepository.UpdateAlignmentAsync(clipId, Arg.Any<double>())
            .Returns(Task.FromException<SessionVideoClip?>(new IOException("database unavailable")));

        var result = await coordinator.ShiftAlignmentAsync(clipId, 1);

        Assert.Equal(VideoAlignmentFailureReason.Save, Assert.IsType<VideoAlignmentResult.Failed>(result).Reason);
        await videoStore.DidNotReceive().PublishClipsAsync(Arg.Any<IReadOnlyCollection<SessionVideoSnapshot>>());
    }

    private void SelectVideo(double durationSeconds)
    {
        SelectFile();
        metadataReader.ReadAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new VideoMetadataReadResult.Parsed(new VideoMetadata(VideoStart, TimeSpan.FromSeconds(durationSeconds))));
    }

    private void SelectFile()
    {
        var file = Substitute.For<IStorageFile>();
        file.Name.Returns("ride.mp4");
        file.Path.Returns(new Uri("file:///videos/ride.mp4"));
        file.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        filesService.OpenVideoFileAsync().Returns(file);
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
