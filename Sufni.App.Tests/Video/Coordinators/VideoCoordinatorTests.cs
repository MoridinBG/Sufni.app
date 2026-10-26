using Avalonia.Platform.Storage;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
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
    private readonly VideoCoordinator coordinator;

    public VideoCoordinatorTests()
    {
        coordinator = new VideoCoordinator(
            filesService,
            metadataReader,
            sessionRepository,
            videoRepository,
            videoStore,
            new InlineBackgroundTaskRunner());
        videoRepository
            .LinkAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<double>(), Arg.Any<long>(), Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(4)
                .Select(sessionId => new SessionVideoClip { Id = Guid.NewGuid(), SessionId = sessionId })
                .ToList());
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
            Arg.Any<long>(),
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Order().SequenceEqual(new[] { inside.Id, overlappingStart.Id }.Order())));
        await videoStore.Received(1).PublishClipsAsync(Arg.Is<IReadOnlyCollection<SessionVideoClip>>(clips => clips.Count == 2));
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

        Assert.IsType<VideoImportResult.Failed>(result);
        await AssertNothingLinkedAsync();
    }

    [Fact]
    public async Task ImportAsync_Fails_WhenNoSessionOverlapsVideo()
    {
        SelectVideo(durationSeconds: 60);
        sessionRepository.GetSessionsAsync().Returns([CreateSession(VideoStart.AddHours(1), durationSeconds: 60)]);

        var result = await coordinator.ImportAsync(TestContext.Current.CancellationToken);

        Assert.IsType<VideoImportResult.Failed>(result);
        await AssertNothingLinkedAsync();
    }

    [Fact]
    public async Task ShiftAlignmentAsync_AddsDeltaToCurrentOffset_AndPublishesClip()
    {
        var clip = new SessionVideoClip { Id = Guid.NewGuid(), AlignmentOffsetSeconds = 3.5 };
        videoStore.Get(clip.Id).Returns(SessionVideoSnapshot.From(new SessionVideoClip { Id = clip.Id, AlignmentOffsetSeconds = 2 }));
        videoRepository.UpdateAlignmentAsync(clip.Id, 3.5).Returns(clip);

        var shifted = await coordinator.ShiftAlignmentAsync(clip.Id, 1.5);

        Assert.True(shifted);
        await videoStore.Received(1).PublishClipsAsync(Arg.Is<IReadOnlyCollection<SessionVideoClip>>(clips => clips.Single() == clip));
    }

    [Fact]
    public async Task ShiftAlignmentAsync_ReturnsFalse_WhenClipIsGone()
    {
        var clipId = Guid.NewGuid();
        videoStore.Get(clipId).Returns(SessionVideoSnapshot.From(new SessionVideoClip { Id = clipId }));
        videoRepository.UpdateAlignmentAsync(clipId, Arg.Any<double>()).Returns((SessionVideoClip?)null);

        var shifted = await coordinator.ShiftAlignmentAsync(clipId, 1);

        Assert.False(shifted);
        await videoStore.DidNotReceive().PublishClipsAsync(Arg.Any<IReadOnlyCollection<SessionVideoClip>>());
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

    private static Session CreateSession(DateTimeOffset start, double durationSeconds) =>
        new(Guid.NewGuid(), "ride", string.Empty, setup: null, start.ToUnixTimeSeconds())
        {
            DurationSeconds = durationSeconds,
        };

    private async Task AssertNothingLinkedAsync()
    {
        await videoRepository.DidNotReceive().LinkAsync(
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<double>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyCollection<Guid>>());
    }
}
