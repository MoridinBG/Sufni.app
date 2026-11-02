using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Services;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Setups.Services;

public class SetupFileServiceTests
{
    [Fact]
    public async Task ExportAndRead_RoundTripSetupAndEmbeddedBikeWithNewIdentities()
    {
        var files = Substitute.For<IFilesService>();
        var file = Substitute.For<IStorageFile>();
        using var output = new MemoryStream();
        file.OpenWriteAsync().Returns(output);
        files.SaveSetupFileAsync(Arg.Any<string>()).Returns(file);
        var bike = new Bike(Guid.NewGuid(), "bike") { ForkStroke = 160, HeadAngle = 65 };
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id, FrontSensorConfigurationJson = "sensor configuration" };
        var board = Guid.NewGuid();
        var sut = new SetupFileService(files, new InlineBackgroundTaskRunner());

        var exported = await sut.ExportAsync(setup, bike, board, TestContext.Current.CancellationToken);
        Assert.IsType<SetupExportResult.Exported>(exported);
        file.OpenReadAsync().Returns(_ => new MemoryStream(output.ToArray()));
        files.OpenSetupFileAsync().Returns(file);
        var imported = Assert.IsType<SetupFileReadResult.Read>(await sut.ReadAsync(TestContext.Current.CancellationToken)).Payload;

        Assert.NotEqual(setup.Id, imported.Setup.Id);
        Assert.NotEqual(bike.Id, imported.Bike.Id);
        Assert.Equal(imported.Bike.Id, imported.Setup.BikeId);
        Assert.Equal(setup.Name, imported.Setup.Name);
        Assert.Equal(setup.FrontSensorConfigurationJson, imported.Setup.FrontSensorConfigurationJson);
        Assert.Equal(board, imported.BoardId);
        Assert.Equal(160, imported.Bike.ForkStroke);
        Assert.Equal(65, imported.Bike.HeadAngle);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"bike\":{\"schema_version\":1,\"rear_suspension\":{\"kind\":\"hardtail\"}}}")]
    public async Task Read_RejectsMalformedOrUnsupportedFile(string json)
    {
        var files = Substitute.For<IFilesService>();
        var file = TestStorageItems.TextFile("setup.json", json);
        files.OpenSetupFileAsync().Returns(file);
        var sut = new SetupFileService(files, new InlineBackgroundTaskRunner());

        Assert.IsType<SetupFileReadResult.InvalidFile>(await sut.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_ReturnsTypedFailureWhenPickerFails()
    {
        var files = Substitute.For<IFilesService>();
        files.OpenSetupFileAsync().Returns<IStorageFile?>(_ => throw new IOException("picker diagnostic"));
        var sut = new SetupFileService(files, new InlineBackgroundTaskRunner());

        var result = Assert.IsType<SetupFileReadResult.Failed>(await sut.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SetupImportFailureReason.Import, result.Reason);
        Assert.Equal("picker diagnostic", result.Details);
    }

    [Fact]
    public async Task Read_CanceledBeforeBackgroundRead_DoesNotOpenTheFile()
    {
        var files = Substitute.For<IFilesService>();
        var file = TestStorageItems.TextFile("setup.json", "{}");
        files.OpenSetupFileAsync().Returns(file);
        var runner = new Sufni.App.Tests.TestSupport.Async.DeferredBackgroundTaskRunner();
        var sut = new SetupFileService(files, runner);
        using var cancellation = new CancellationTokenSource();
        var pending = sut.ReadAsync(cancellation.Token);

        cancellation.Cancel();
        await runner.RunNextAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        await file.DidNotReceive().OpenReadAsync();
    }

    [Fact]
    public async Task Export_ReturnsTypedFailureWhenFileCannotBeWritten()
    {
        var files = Substitute.For<IFilesService>();
        var file = Substitute.For<IStorageFile>();
        file.OpenWriteAsync().Returns<Stream>(_ => throw new IOException("write diagnostic"));
        files.SaveSetupFileAsync(Arg.Any<string>()).Returns(file);
        var sut = new SetupFileService(files, new InlineBackgroundTaskRunner());

        var result = Assert.IsType<SetupExportResult.Failed>(await sut.ExportAsync(
            new Setup(Guid.NewGuid(), "setup"), new Bike(Guid.NewGuid(), "bike"), null, TestContext.Current.CancellationToken));

        Assert.Equal(SetupExportFailureReason.Export, result.Reason);
        Assert.Equal("write diagnostic", result.Details);
    }
}
