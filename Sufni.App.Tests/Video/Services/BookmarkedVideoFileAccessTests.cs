using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Video.Services;

namespace Sufni.App.Tests.Video.Services;

public class BookmarkedVideoFileAccessTests
{
    private readonly IFilesService files = Substitute.For<IFilesService>();
    private readonly InMemorySecureStorage storage = new();
    private BookmarkedVideoFileAccess Create() => new(files, storage, new InlineBackgroundTaskRunner());

    [Fact]
    public async Task SavedBookmark_RestoresAfterServiceRecreation_AndHoldsAccessUntilLeaseDisposal()
    {
        var selected = Substitute.For<IStorageFile>();
        selected.CanBookmark.Returns(true);
        selected.SaveBookmarkAsync().Returns("opaque-grant");
        var saved = await Create().RememberAsync(selected, "/videos/original.mov", TestContext.Current.CancellationToken);
        Assert.IsType<VideoAccessSaveResult.Saved>(saved);
        var restored = Substitute.For<IStorageBookmarkFile>();
        restored.Path.Returns(new Uri("file:///videos/resolved.mov"));
        using var stream = new MemoryStream([1]);
        restored.OpenReadAsync().Returns(stream);
        files.OpenBookmarkedVideoAsync("opaque-grant", Arg.Any<CancellationToken>()).Returns(restored);

        var lease = await Create().AcquireAsync("/videos/original.mov", TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.Equal("/videos/resolved.mov", lease.Path);
        Assert.True(stream.CanRead);
        lease.Dispose();
        Assert.False(stream.CanRead);
        restored.Received(1).Dispose();
    }

    [Fact]
    public async Task UnreadableBookmark_ReturnsUnavailable_AndReleasesRestoredFile()
    {
        var selected = Substitute.For<IStorageFile>();
        selected.CanBookmark.Returns(true);
        selected.SaveBookmarkAsync().Returns("grant");
        await Create().RememberAsync(selected, "/videos/ride.mov", TestContext.Current.CancellationToken);
        var restored = Substitute.For<IStorageBookmarkFile>();
        restored.Path.Returns(new Uri("file:///videos/ride.mov"));
        restored.OpenReadAsync().Returns(Task.FromException<Stream>(new UnauthorizedAccessException()));
        files.OpenBookmarkedVideoAsync("grant", Arg.Any<CancellationToken>()).Returns(restored);

        Assert.Null(await Create().AcquireAsync("/videos/ride.mov", TestContext.Current.CancellationToken));
        restored.Received(1).Dispose();
    }

    [Fact]
    public async Task CancelledRestore_ReleasesStreamAndFile()
    {
        var selected = Substitute.For<IStorageFile>();
        selected.CanBookmark.Returns(true);
        selected.SaveBookmarkAsync().Returns("grant");
        await Create().RememberAsync(selected, "/videos/ride.mov", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        using var stream = new MemoryStream([1]);
        var restored = Substitute.For<IStorageBookmarkFile>();
        restored.Path.Returns(new Uri("file:///videos/ride.mov"));
        restored.OpenReadAsync().Returns(_ => { cancellation.Cancel(); return Task.FromResult<Stream>(stream); });
        files.OpenBookmarkedVideoAsync("grant", Arg.Any<CancellationToken>()).Returns(restored);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().AcquireAsync("/videos/ride.mov", cancellation.Token));
        Assert.False(stream.CanRead);
        restored.Received(1).Dispose();
    }

    [Fact]
    public async Task SelectionWithoutBookmark_ReturnsAccessFailure()
    {
        var file = Substitute.For<IStorageFile>();
        Assert.IsType<VideoAccessSaveResult.Failed>(await Create().RememberAsync(file, "/videos/ride.mov", TestContext.Current.CancellationToken));
    }
}
