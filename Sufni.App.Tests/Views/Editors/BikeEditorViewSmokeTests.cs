using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.BikeEditing;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Views.Editors;

public class BikeEditorViewSmokeTests
{
    [AvaloniaFact]
    public async Task BikeEditorView_ShowsWithPopulatedBikeEditorViewModel()
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new BikeEditorView
        {
            DataContext = CreateViewModel()
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var bikeImage = view.FindControl<Image>("BikeImage");

        Assert.NotNull(bikeImage);
        Assert.NotNull(bikeImage.Source);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task BikeEditorDesktopView_ShowsWithPopulatedBikeEditorViewModel()
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new BikeEditorDesktopView
        {
            DataContext = CreateViewModel()
        };
        var host = new Window
        {
            Width = 1200,
            Height = 800,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Single(view.GetVisualDescendants().OfType<SplitView>());

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task BikeEditorView_HidesShockStrokeAndPlot_WhenBikeIsHardtail()
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new BikeEditorView
        {
            DataContext = CreateViewModel(TestSnapshots.Bike())
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(view.FindControl<TextBlock>("ShockStrokeLabel")!.IsVisible);
        Assert.False(view.FindControl<Grid>("LeverageRatioPlotGrid")!.IsVisible);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task BikeImageControlsDesktopView_HidesShockStrokeAndPlot_WhenBikeIsHardtail()
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new BikeImageControlsDesktopView
        {
            DataContext = CreateViewModel(TestSnapshots.Bike())
        };
        var host = new Window
        {
            Width = 500,
            Height = 800,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(view.FindControl<TextBlock>("ShockStrokeLabel")!.IsVisible);
        Assert.False(view.FindControl<Grid>("LeverageRatioPlotGrid")!.IsVisible);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task BikeImageControlsDesktopView_ShowsWheelRemoveButtons_AndBindsCommands()
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var viewModel = CreateViewModel();
        var view = new BikeImageControlsDesktopView
        {
            DataContext = viewModel
        };
        var host = new Window
        {
            Width = 500,
            Height = 800,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var removeButtons = view.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Content as string == "X")
            .ToArray();

        Assert.Equal(2, removeButtons.Length);
        Assert.All(removeButtons, button => Assert.NotNull(button.Command));

        removeButtons[0].Command!.Execute(removeButtons[0].CommandParameter);
        removeButtons[1].Command!.Execute(removeButtons[1].CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(viewModel.WheelGeometry.FrontWheelDiameter);
        Assert.Null(viewModel.WheelGeometry.RearWheelDiameter);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task BikeImageControlsDesktopView_RendersLeverageRatioEditor_WhenInLeverageRatioMode()
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new BikeImageControlsDesktopView
        {
            DataContext = CreateViewModel(CreateLeverageRatioSnapshot())
        };
        var host = new Window
        {
            Width = 500,
            Height = 800,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Single(view.GetVisualDescendants().OfType<LeverageRatioEditorView>());
        Assert.DoesNotContain(
            view.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text?.Contains("Sufni.App.ViewModels.Editors.Bike.LeverageRatioEditorViewModel", StringComparison.Ordinal) == true);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task BikeImageControlsDesktopView_HidesWheelEditors_WhenInLeverageRatioMode()
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new BikeImageControlsDesktopView
        {
            DataContext = CreateViewModel(CreateLeverageRatioSnapshot())
        };
        var host = new Window
        {
            Width = 500,
            Height = 800,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(view.FindControl<Grid>("FrontWheelEditorHeaderGrid")!.IsVisible);
        Assert.False(view.FindControl<Grid>("FrontWheelEditorInputsGrid")!.IsVisible);
        Assert.False(view.FindControl<Grid>("RearWheelEditorHeaderGrid")!.IsVisible);
        Assert.False(view.FindControl<Grid>("RearWheelEditorInputsGrid")!.IsVisible);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static BikeEditorViewModel CreateViewModel(BikeSnapshot? snapshot = null, IDialogService? dialogService = null)
    {
        var bikeCoordinator = Substitute.For<IBikeCoordinator>();
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<RearSuspension?>(), Arg.Any<CancellationToken>())
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
            snapshot ?? CreateSnapshot(),
            isNew: false,
            bikeCoordinator,
            dependencyQuery,
            Substitute.For<IShellCoordinator>(),
            dialogService ?? Substitute.For<IDialogService>());
    }

    private static BikeSnapshot CreateSnapshot()
    {
        var bike = new Bike(Guid.NewGuid(), "view bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = 0.5,
            RearSuspensionKind = RearSuspensionKind.Linkage,
            Chainstay = 440,
            PixelsToMillimeters = 1,
            Linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true),
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            ImageRotationDegrees = 12.5,
            Image = TestImages.SmallPng(),
            Updated = 1,
        };

        return BikeSnapshot.From(bike);
    }

    private static BikeSnapshot CreateLeverageRatioSnapshot()
    {
        return TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (30, 75), (60, 150))) with
        {
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5),
        };
    }

    private static void EnsureBikeEditorViewResources()
    {
        var resources = Avalonia.Application.Current?.Resources
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        resources["SufniAccentColor"] = Brushes.CornflowerBlue;
        resources["SufniRegion"] = Brushes.Gray;
        resources["SufniForeground"] = Brushes.White;
        resources["SufniBackgroundDisabled"] = Brushes.DimGray;
        resources["SufniBorderBrush"] = Brushes.Black;
        resources["SufniDangerColor"] = Brushes.Red;
        resources["SufniDangerColorDark"] = Brushes.DarkRed;
    }
}