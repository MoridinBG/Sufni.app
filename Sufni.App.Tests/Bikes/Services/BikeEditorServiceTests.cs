using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.Kinematics;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Services;

[Collection("Ui")]
public class BikeEditorServiceTests
{
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();

    private BikeEditorService CreateService() => new(filesService, backgroundTaskRunner);


    [AvaloniaFact]
    public async Task LoadImageAsync_DecodesBitmapFromStreamWithoutUsingPath()
    {
        var imageBytes = TestImages.SmallPngBytes();

        var file = Substitute.For<IStorageFile>();
        file.Path.Returns(_ => throw new InvalidOperationException("Path should not be used for image loading."));
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(imageBytes)));
        filesService.OpenBikeImageFileAsync().Returns(file);

        var result = await CreateService().LoadImageAsync();

        var loaded = Assert.IsType<BikeImageLoadResult.Loaded>(result);
        Assert.Equal(imageBytes, loaded.ImageBytes);
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
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            ImageRotationDegrees = 12.5,
        }.ToJson();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(bikeJson))));
        filesService.OpenBikeFileAsync().Returns(file);

        var result = await CreateService().ImportBikeAsync();

        var imported = Assert.IsType<BikeFileImportResult.Imported>(result);
        Assert.Equal("imported bike", imported.Bike.Name);
        Assert.Equal(64, imported.Bike.HeadAngle);
        Assert.Equal(150, imported.Bike.ForkStroke);
        Assert.Equal(EtrtoRimSize.Inch29, imported.Bike.FrontWheelRimSize);
        Assert.Equal(2.4, imported.Bike.FrontWheelTireWidth);
        Assert.Equal(TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4), imported.Bike.FrontWheelDiameterMm);
        Assert.Equal(EtrtoRimSize.Inch275, imported.Bike.RearWheelRimSize);
        Assert.Equal(2.5, imported.Bike.RearWheelTireWidth);
        Assert.Equal(TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5), imported.Bike.RearWheelDiameterMm);
        Assert.Equal(12.5, imported.Bike.ImageRotationDegrees);
    }

    [Fact]
    public async Task ImportBikeAsync_ReturnsInvalidFile_WhenJsonIsInvalid()
    {
        var file = Substitute.For<IStorageFile>();
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("not json"))));
        filesService.OpenBikeFileAsync().Returns(file);

        var result = await CreateService().ImportBikeAsync();

        var invalid = Assert.IsType<BikeFileImportResult.InvalidFile>(result);
        Assert.False(string.IsNullOrWhiteSpace(invalid.ErrorMessage));
    }

    [Fact]
    public async Task ExportBikeAsync_WritesSerializedBikeJson()
    {
        var output = new MemoryStream();
        var file = Substitute.For<IStorageFile>();
        file.OpenWriteAsync().Returns(Task.FromResult<Stream>(output));
        filesService.SaveBikeFileAsync(Arg.Any<string>()).Returns(file);

        var bike = new Bike(Guid.NewGuid(), "exported bike")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            FrontWheelDiameterMm = 760,
            RearWheelDiameterMm = 750,
            ImageRotationDegrees = 13.5,
        };

        var result = await CreateService().ExportBikeAsync(bike);

        Assert.IsType<BikeExportResult.Exported>(result);
        var json = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("exported bike", json);
        Assert.Contains("schema_version", json);
        Assert.Contains("head_angle", json);
        Assert.Contains("front_wheel", json);
        Assert.Contains("rear_wheel", json);
        Assert.Contains("diameter_mm", json);
        Assert.Contains("image_rotation_degrees", json);
    }

    [Fact]
    public async Task LoadAnalysisAsync_ReturnsUnavailable_WhenRearSuspensionIsHardtail()
    {
        var result = await CreateService().LoadAnalysisAsync(new RearSuspensionSpec.Hardtail());

        Assert.IsType<BikeEditorAnalysisResult.Unavailable>(result);
    }

    [Fact]
    public async Task LoadAnalysisAsync_ReturnsComputed_WhenLinkageIsValid()
    {
        var result = await CreateService().LoadAnalysisAsync(new RearSuspensionSpec.Linkage(CreateSimpleLinkageSpec()));

        var computed = Assert.IsType<BikeEditorAnalysisResult.Computed>(result);
        Assert.NotEmpty(computed.Data.LeverageRatioData.X);
        Assert.NotEmpty(computed.Data.LeverageRatioData.Y);
        Assert.True(computed.Data.RearAxlePathData.HasValue);
        var rearAxlePathData = computed.Data.RearAxlePathData.Value;
        Assert.NotEmpty(rearAxlePathData.X);
        Assert.NotEmpty(rearAxlePathData.Y);
    }

    [Fact]
    public async Task LoadAnalysisAsync_ReturnsComputed_WhenLeverageRatioIsValid()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve(
            (0, 0),
            (10, 25),
            (20, 45));

        var result = await CreateService().LoadAnalysisAsync(new RearSuspensionSpec.LeverageRatio(leverageRatio));

        var computed = Assert.IsType<BikeEditorAnalysisResult.Computed>(result);
        Assert.Collection(
            computed.Data.LeverageRatioData.X,
            wheelTravel => Assert.Equal(12.5, wheelTravel),
            wheelTravel => Assert.Equal(35, wheelTravel));
        Assert.Collection(
            computed.Data.LeverageRatioData.Y,
            ratio => Assert.Equal(2.5, ratio),
            ratio => Assert.Equal(2, ratio));
        Assert.Null(computed.Data.RearAxlePathData);
    }

    private static LinkageSpec CreateSimpleLinkageSpec()
    {
        var mapping = new JointNameMapping();
        return new LinkageSpec(
            [
                new JointSpec(mapping.BottomBracket, JointType.BottomBracket, 0, 0),
                new JointSpec(mapping.RearWheel, JointType.RearWheel, 4, 0),
                new JointSpec(mapping.ShockEye1, JointType.Floating, 4, 3),
                new JointSpec(mapping.ShockEye2, JointType.Fixed, 0, 3)
            ],
            [
                new LinkSpec(mapping.BottomBracket, mapping.RearWheel),
                new LinkSpec(mapping.RearWheel, mapping.ShockEye1)
            ],
            new LinkSpec(mapping.ShockEye1, mapping.ShockEye2),
            0.5);
    }
}
