using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;

namespace Sufni.App.Tests.LiveDaq.Services;

public class DeviceConfigFileServiceTests
{
    [Fact]
    public async Task SelectAsync_ReadsSelectedBytesAndReleasesStream()
    {
        var files = Substitute.For<IFilesService>();
        var stream = new MemoryStream([1, 2, 3]);
        files.OpenDeviceConfigFileAsync(Arg.Any<CancellationToken>())
            .Returns(new PickedFile("CONFIG", () => Task.FromResult<Stream>(stream)));
        var service = new DeviceConfigFileService(files, new InlineBackgroundTaskRunner());

        var selected = await service.SelectAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(selected);
        Assert.Equal("CONFIG", selected.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, selected.Bytes);
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task SelectAsync_DismissedPickerReturnsNoConfiguration()
    {
        var files = Substitute.For<IFilesService>();
        files.OpenDeviceConfigFileAsync(Arg.Any<CancellationToken>()).Returns((PickedFile?)null);
        var service = new DeviceConfigFileService(files, new InlineBackgroundTaskRunner());

        Assert.Null(await service.SelectAsync(TestContext.Current.CancellationToken));
    }
}
