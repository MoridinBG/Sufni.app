using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NSubstitute;
using Sufni.App.BikeEditing;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Kinematics;

namespace Sufni.Screenshots;

/// <summary>
/// Captures screenshots of the bike editor at successive creation stages.
/// Run with: <c>dotnet test Sufni.Screenshots --filter BikeCreationScreenshots</c>
/// </summary>
public class BikeCreationScreenshots
{
    private static readonly string OutputDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "screenshots",
        "bike-creation");

    [AvaloniaFact]
    public async Task Step01_NewEmptyBike()
    {
        var snapshot = new BikeSnapshot(
            Id: Guid.NewGuid(),
            Name: "new bike",
            HeadAngle: 0,
            ForkStroke: null,
            ShockStroke: null,
            Chainstay: null,
            PixelsToMillimeters: 0,
            FrontWheelDiameterMm: null,
            RearWheelDiameterMm: null,
            FrontWheelRimSize: null,
            FrontWheelTireWidth: null,
            RearWheelRimSize: null,
            RearWheelTireWidth: null,
            ImageRotationDegrees: 0,
            Linkage: null,
            Image: null,
            Updated: 1);

        var vm = CreateViewModel(snapshot, isNew: true);
        await CaptureControlsPanel(vm, "01-new-bike.png");
    }

    [AvaloniaFact]
    public async Task Step02_NameAndBasicGeometry()
    {
        var snapshot = new BikeSnapshot(
            Id: Guid.NewGuid(),
            Name: "Trail Shredder",
            HeadAngle: 64.5,
            ForkStroke: 170,
            ShockStroke: null,
            Chainstay: null,
            PixelsToMillimeters: 0,
            FrontWheelDiameterMm: null,
            RearWheelDiameterMm: null,
            FrontWheelRimSize: null,
            FrontWheelTireWidth: null,
            RearWheelRimSize: null,
            RearWheelTireWidth: null,
            ImageRotationDegrees: 0,
            Linkage: null,
            Image: null,
            Updated: 1);

        var vm = CreateViewModel(snapshot);
        await CaptureControlsPanel(vm, "02-name-and-geometry.png");
    }

    [AvaloniaFact]
    public async Task Step03_FullSuspensionFields()
    {
        var snapshot = new BikeSnapshot(
            Id: Guid.NewGuid(),
            Name: "Trail Shredder",
            HeadAngle: 64.5,
            ForkStroke: 170,
            ShockStroke: 57.5,
            Chainstay: 440,
            PixelsToMillimeters: 0,
            FrontWheelDiameterMm: null,
            RearWheelDiameterMm: null,
            FrontWheelRimSize: null,
            FrontWheelTireWidth: null,
            RearWheelRimSize: null,
            RearWheelTireWidth: null,
            ImageRotationDegrees: 0,
            Linkage: null,
            Image: SmallTestImage(),
            Updated: 1);

        var vm = CreateViewModel(snapshot);
        await CaptureControlsPanel(vm, "03-full-suspension.png");
    }

    [AvaloniaFact]
    public async Task Step04_WheelsConfigured()
    {
        var snapshot = new BikeSnapshot(
            Id: Guid.NewGuid(),
            Name: "Trail Shredder",
            HeadAngle: 64.5,
            ForkStroke: 170,
            ShockStroke: 57.5,
            Chainstay: 440,
            PixelsToMillimeters: 0,
            FrontWheelDiameterMm: WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            RearWheelDiameterMm: WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            FrontWheelRimSize: EtrtoRimSize.Inch29,
            FrontWheelTireWidth: 2.4,
            RearWheelRimSize: EtrtoRimSize.Inch275,
            RearWheelTireWidth: 2.5,
            ImageRotationDegrees: 0,
            Linkage: null,
            Image: SmallTestImage(),
            Updated: 1);

        var vm = CreateViewModel(snapshot);
        await CaptureControlsPanel(vm, "04-wheels-configured.png");
    }

    #region Helpers

    private static async Task CaptureControlsPanel(BikeEditorViewModel vm, string filename)
    {
        var view = new BikeImageControlsDesktopView { DataContext = vm };
        var window = new Window
        {
            Width = 420,
            Height = 900,
            Content = view
        };

        window.Show();
        await FlushAsync();
        await FlushAsync();

        var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);

        Directory.CreateDirectory(OutputDir);
        var path = Path.Combine(OutputDir, filename);
        bitmap.Save(path);

        window.Close();
        await FlushAsync();
    }

    private static BikeEditorViewModel CreateViewModel(BikeSnapshot snapshot, bool isNew = false)
    {
        var bikeCoordinator = Substitute.For<IBikeCoordinator>();
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));
        bikeCoordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(new BikeImageLoadResult.Canceled()));
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(new BikeImportResult.Canceled()));
        bikeCoordinator.ExportBikeAsync(Arg.Any<Bike>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeExportResult>(new BikeExportResult.Canceled()));

        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();
        dependencyQuery.Changes.Returns(Observable.Empty<Unit>());
        dependencyQuery.IsBikeInUse(Arg.Any<Guid>()).Returns(false);

        return new BikeEditorViewModel(
            snapshot,
            isNew,
            bikeCoordinator,
            dependencyQuery,
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>());
    }

    private static async Task FlushAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static WriteableBitmap SmallTestImage()
    {
        return new WriteableBitmap(
            new PixelSize(100, 60),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
    }

    private static double WheelDiameter(EtrtoRimSize rimSize, double tireWidth) =>
        Math.Round(rimSize.CalculateTotalDiameterMm(tireWidth), 1);

    #endregion
}
