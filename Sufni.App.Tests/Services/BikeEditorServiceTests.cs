using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.App.BikeEditing;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Services;

public class BikeEditorServiceTests
{
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();

    private BikeEditorService CreateService() => new(filesService, backgroundTaskRunner);

    [AvaloniaFact]
    public async Task LoadImageAsync_DecodesBitmapFromStreamWithoutUsingPath()
    {
        var sourceBitmap = TestImages.SmallPng();
        var imageBytes = new MemoryStream();
        sourceBitmap.Save(imageBytes);
        imageBytes.Position = 0;

        var file = Substitute.For<IStorageFile>();
        file.Path.Returns(_ => throw new InvalidOperationException("Path should not be used for image loading."));
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(imageBytes.ToArray())));
        filesService.OpenBikeImageFileAsync().Returns(file);

        var result = await CreateService().LoadImageAsync();

        var loaded = Assert.IsType<BikeImageLoadResult.Loaded>(result);
        Assert.Equal(1, loaded.Bitmap.Size.Width);
        Assert.Equal(1, loaded.Bitmap.Size.Height);
    }

    [Fact]
    public async Task ImportBikeAsync_ReturnsImportedBike_WhenJsonIsValid()
    {
        var file = Substitute.For<IStorageFile>();
        var bikeJson = new Bike(Guid.NewGuid(), "imported bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
        }.ToJson();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(bikeJson))));
        filesService.OpenBikeFileAsync().Returns(file);

        var result = await CreateService().ImportBikeAsync();

        var imported = Assert.IsType<BikeFileImportResult.Imported>(result);
        Assert.Equal("imported bike", imported.Bike.Name);
        Assert.Equal(64, imported.Bike.HeadAngle);
        Assert.Equal(150, imported.Bike.ForkStroke);
    }

    [Fact]
    public async Task ImportBikeAsync_ReturnsInvalidFile_WhenJsonIsInvalid()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("not json"))));
        filesService.OpenBikeFileAsync().Returns(file);

        var result = await CreateService().ImportBikeAsync();

        var invalid = Assert.IsType<BikeFileImportResult.InvalidFile>(result);
        Assert.Equal("JSON file was not a valid bike file.", invalid.ErrorMessage);
    }

    [Fact]
    public async Task ExportBikeAsync_WritesSerializedBikeJson()
    {
        var output = new MemoryStream();
        var file = Substitute.For<IStorageFile>();
        file.OpenWriteAsync().Returns(Task.FromResult<Stream>(output));
        filesService.SaveBikeFileAsync().Returns(file);

        var bike = new Bike(Guid.NewGuid(), "exported bike")
        {
            HeadAngle = 65,
            ForkStroke = 160,
        };

        var result = await CreateService().ExportBikeAsync(bike);

        Assert.IsType<BikeExportResult.Exported>(result);
        var json = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("exported bike", json);
        Assert.Contains("head_angle", json);
    }

    [Fact]
    public async Task AnalyzeLinkageAsync_ReturnsUnavailable_WhenLinkageMissing()
    {
        var result = await CreateService().AnalyzeLinkageAsync(null);

        Assert.IsType<BikeEditorAnalysisResult.Unavailable>(result);
    }

    [Fact]
    public async Task AnalyzeLinkageAsync_ReturnsComputed_WhenLinkageIsValid()
    {
        var result = await CreateService().AnalyzeLinkageAsync(CreateSimpleLinkage());

        var computed = Assert.IsType<BikeEditorAnalysisResult.Computed>(result);
        Assert.NotEmpty(computed.Data.LeverageRatioData.X);
        Assert.NotEmpty(computed.Data.LeverageRatioData.Y);
    }

    private static Linkage CreateSimpleLinkage()
    {
        var mapping = new JointNameMapping();
        var bottomBracket = new Joint(mapping.BottomBracket, JointType.BottomBracket, 0, 0);
        var rearWheel = new Joint(mapping.RearWheel, JointType.RearWheel, 4, 0);
        var shockEye1 = new Joint(mapping.ShockEye1, JointType.Floating, 4, 3);
        var shockEye2 = new Joint(mapping.ShockEye2, JointType.Fixed, 0, 3);

        var linkage = new Linkage
        {
            Joints = [bottomBracket, rearWheel, shockEye1, shockEye2],
            Links =
            [
                new Link(bottomBracket, rearWheel),
                new Link(rearWheel, shockEye1),
            ],
            Shock = new Link(shockEye1, shockEye2),
            ShockStroke = 0.5,
        };
        linkage.ResolveJoints();
        return linkage;
    }

    private sealed class InlineBackgroundTaskRunner : IBackgroundTaskRunner
    {
        public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

        public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default) => Task.FromResult(work());

        public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) => work();
    }
}