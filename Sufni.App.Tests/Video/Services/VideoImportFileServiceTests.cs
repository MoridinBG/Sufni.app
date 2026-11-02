using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Video.Services;

namespace Sufni.App.Tests.Video.Services;

public class VideoImportFileServiceTests
{
    private readonly IFilesService files = Substitute.For<IFilesService>();
    private readonly IVideoMetadataReader metadata = Substitute.For<IVideoMetadataReader>();
    private readonly IVideoFileAccess access = Substitute.For<IVideoFileAccess>();
    private VideoImportFileService Create() => new(files, metadata, access, new InlineBackgroundTaskRunner());

    [Fact]
    public async Task ReadAsync_PreparesMetadataAndRetainsAccess_ThenDisposesSelection()
    {
        var file = SelectFile();
        var timing = new VideoMetadata(DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(1));
        metadata.ReadAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(new VideoMetadataReadResult.Parsed(timing));
        access.RememberAsync(file, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new VideoAccessSaveResult.Saved());

        var result = Assert.IsType<VideoFileReadResult.Read>(await Create().ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(timing, result.Metadata);
        Assert.Equal("/videos/ride.mp4", result.Path);
        file.Received(1).Dispose();
    }

    [Fact]
    public async Task ReadAsync_InvalidMetadataDoesNotRetainAccess()
    {
        SelectFile();
        metadata.ReadAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(new VideoMetadataReadResult.Invalid("invalid header"));

        var result = Assert.IsType<VideoFileReadResult.Failed>(await Create().ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(VideoImportFailureReason.InvalidMetadata, result.Reason);
        await access.DidNotReceive().RememberAsync(Arg.Any<IStorageFile>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_CannotRetainAccess_ReturnsTypedFailure()
    {
        var file = SelectFile();
        metadata.ReadAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(new VideoMetadataReadResult.Parsed(new(DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(1))));
        access.RememberAsync(file, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new VideoAccessSaveResult.Failed("grant unavailable"));

        var result = Assert.IsType<VideoFileReadResult.Failed>(await Create().ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(VideoImportFailureReason.Access, result.Reason);
        file.Received(1).Dispose();
    }

    [Fact]
    public async Task ReadAsync_DismissedPicker_ReturnsCancelled()
    {
        files.OpenVideoFileAsync().Returns((IStorageFile?)null);
        Assert.IsType<VideoFileReadResult.Cancelled>(await Create().ReadAsync(TestContext.Current.CancellationToken));
    }

    private IStorageFile SelectFile()
    {
        var file = TestStorageItems.TextFile("ride.mp4", "video");
        file.Path.Returns(new Uri("file:///videos/ride.mp4"));
        files.OpenVideoFileAsync().Returns(file);
        return file;
    }
}
