using Sufni.App.Shared.Models;
using System.IO;
using System.Text;
using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.Kinematics;

using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Bikes.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Services;

public class BikeEditorServiceTests
{
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();

    private BikeEditorService CreateService(IKinematicSolutionCache? kinematicSolutionCache = null) =>
        new(filesService, backgroundTaskRunner, kinematicSolutionCache ?? new KinematicSolutionCache());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadImageAsync_ReturnsTypedFailure_WhenPickerOrReadFails(bool pickerFails)
    {
        var error = new IOException("unavailable image");
        if (pickerFails)
            filesService.OpenBikeImageFileAsync().Returns(Task.FromException<IStorageFile?>(error));
        else
        {
            var file = TestStorageItems.TextFile("image.png", "");
            file.OpenReadAsync().Returns(Task.FromException<Stream>(error));
            filesService.OpenBikeImageFileAsync().Returns(file);
        }

        var result = await CreateService().LoadImageAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BikeImageFailureReason.Read, Assert.IsType<BikeImageLoadResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task LoadImageAsync_LoadsBytesAndFileNameWithoutUsingPath()
    {
        var imageBytes = TestImages.SmallPngBytes();

        var file = Substitute.For<IStorageFile>();
        file.Name.Returns("bike.png");
        file.Path.Returns(_ => throw new InvalidOperationException("Path should not be used for image loading."));
        file.OpenReadAsync().Returns(Task.FromResult<Stream>(new MemoryStream(imageBytes)));
        filesService.OpenBikeImageFileAsync().Returns(file);

        var result = await CreateService().LoadImageAsync(cancellationToken: TestContext.Current.CancellationToken);

        var loaded = Assert.IsType<BikeImageLoadResult.Loaded>(result);
        Assert.Equal(imageBytes, loaded.ImageBytes);
        Assert.Equal("bike.png", loaded.FileName);
    }

    [Fact]
    public async Task ImportBikeAsync_ReturnsImportedBike_WhenJsonIsValid()
    {
        var file = TestStorageItems.TextFile("bike.json", """
            {"schema_version":2,"name":"imported bike","head_angle":64,"fork_stroke":150,
             "rear_suspension":{"kind":"hardtail"},"front_wheel":{"diameter_mm":743.9},
             "damping_speed_cutoffs":{"front":{"compression_mm_per_second":200,"rebound_mm_per_second":200},
                                     "rear":{"compression_mm_per_second":200,"rebound_mm_per_second":200}},
             "image_rotation_degrees":12.5,"image":"AQID"}
            """);
        filesService.OpenBikeFileAsync().Returns(file);

        var result = await CreateService().ImportBikeAsync(TestContext.Current.CancellationToken);

        var imported = Assert.IsType<BikeFileImportResult.Imported>(result).Bike;
        Assert.Equal("imported bike", imported.Name);
        Assert.Equal(743.9, imported.FrontWheelDiameterMm);
        Assert.Equal(12.5, imported.ImageRotationDegrees);
        Assert.Equal(new byte[] { 1, 2, 3 }, imported.ImageBytes);
    }

    [Fact]
    public async Task ImportBikeAsync_ReturnsInvalidFile_WhenJsonIsInvalid()
    {
        var file = TestStorageItems.TextFile("bike.json", "not json");
        filesService.OpenBikeFileAsync().Returns(file);

        var result = await CreateService().ImportBikeAsync(cancellationToken: TestContext.Current.CancellationToken);

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

        var result = await CreateService().ExportBikeAsync(bike, cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<BikeExportResult.Exported>(result);
        using var exportedText = new StreamReader(new MemoryStream(output.ToArray()), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var json = System.Text.Json.JsonDocument.Parse(exportedText.ReadToEnd());
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("exported bike", root.GetProperty("name").GetString());
        Assert.Equal(65, root.GetProperty("head_angle").GetDouble());
        Assert.Equal(160, root.GetProperty("fork_stroke").GetDouble());
        Assert.Equal(760, root.GetProperty("front_wheel").GetProperty("diameter_mm").GetDouble());
        Assert.Equal(750, root.GetProperty("rear_wheel").GetProperty("diameter_mm").GetDouble());
        Assert.Equal(13.5, root.GetProperty("image_rotation_degrees").GetDouble());
    }

    public static TheoryData<RearSuspensionSpec, Type> UnavailableRearSuspensions => new()
    {
        { new RearSuspensionSpec.Hardtail(), typeof(BikeEditorAnalysisResult.NotApplicable) },
        { new RearSuspensionSpec.LinkageDraft(), typeof(BikeEditorAnalysisResult.Unsolvable) },
        { new RearSuspensionSpec.LeverageRatioDraft(), typeof(BikeEditorAnalysisResult.Unsolvable) },
    };

    [Theory]
    [MemberData(nameof(UnavailableRearSuspensions))]
    public async Task LoadAnalysisAsync_ClassifiesUnavailableInputs_WithoutSolving(RearSuspensionSpec rearSuspension, Type expected)
    {
        var result = await CreateService().LoadAnalysisAsync(rearSuspension, TestContext.Current.CancellationToken);

        Assert.IsType(expected, result);
    }

    [Fact]
    public async Task LoadAnalysisAsync_UnexpectedSolverFailure_IsNotReportedAsUnsolvable()
    {
        var cache = new FaultingKinematicSolutionCache();

        var result = await CreateService(cache).LoadAnalysisAsync(new RearSuspensionSpec.Linkage(TestSnapshots.FullSuspensionLinkageSpec()), TestContext.Current.CancellationToken);

        Assert.Equal(BikeAnalysisFailureReason.Analyze, Assert.IsType<BikeEditorAnalysisResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task LoadAnalysisAsync_ReturnsUnsolvable_WhenLinkageCannotBeSolved()
    {
        var linkage = new LinkageSpec(
            [],
            [],
            new LinkSpec("shock-eye-a", "shock-eye-b"),
            0.5);

        var result = await CreateService().LoadAnalysisAsync(new RearSuspensionSpec.Linkage(linkage), cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<BikeEditorAnalysisResult.Unsolvable>(result);
    }

    [Fact]
    public async Task LoadAnalysisAsync_ReturnsComputed_WhenLeverageRatioIsValid()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve(
            (0, 0),
            (10, 25),
            (20, 45));

        var result = await CreateService().LoadAnalysisAsync(new RearSuspensionSpec.LeverageRatio(leverageRatio), cancellationToken: TestContext.Current.CancellationToken);

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

    private sealed class FaultingKinematicSolutionCache : IKinematicSolutionCache
    {
        public KinematicSolution GetOrSolve(LinkageSpec linkage, int steps = 200, int iterations = 1000) =>
            throw new InvalidOperationException("unexpected failure");
    }

}
