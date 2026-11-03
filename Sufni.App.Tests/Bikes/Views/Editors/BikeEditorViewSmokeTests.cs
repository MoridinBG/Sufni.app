using Microsoft.Extensions.Time.Testing;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.Kinematics;

using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.Bikes.DesktopViews.Editors;
using Sufni.App.Bikes.DesktopViews.Items;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Views.Editors;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Views.Editors;

[Collection("Ui")]
public class BikeEditorViewSmokeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopModeButton_FollowsPointerCapability(bool hasPointer)
    {
        EnsureBikeEditorViewResources();
        ViewTestHelpers.EnsurePlotViewStyle();
        var view = new BikeEditorDesktopView { DataContext = CreateViewModel(hasPointer: hasPointer) };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            var splitView = Assert.Single(view.GetVisualDescendants().OfType<SplitView>());
            var controls = Assert.IsType<BikeImageControlsDesktopView>(splitView.Pane);
            Assert.Equal(hasPointer, controls.FindControl<ToggleButton>("LinkageModeButton")!.IsEnabled);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
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

        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(view.FindControl<TextBlock>("ShockStrokeLabel")!.IsVisible);
        Assert.False(view.FindControl<Grid>("LeverageRatioPlotGrid")!.IsVisible);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static BikeEditorViewModel CreateViewModel(BikeSnapshot? snapshot = null, IDialogService? dialogService = null, bool hasPointer = true)
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();
        dependencyQuery.Changes.Returns(Observable.Empty<Unit>());
        dependencyQuery.IsBikeInUse(Arg.Any<Guid>()).Returns(false);

        snapshot ??= CreateSnapshot();
        bikeCoordinator.LoadDraftAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(
            new BikeDraftLoadResult.Loaded(BikeDraft.From(snapshot, snapshot.HasImage ? TestImages.SmallPngBytes() : [])));
        var editor = new BikeEditorViewModel(
            snapshot,
            isNew: false,
            bikeCoordinator,
            dependencyQuery,
            Substitute.For<IShellCoordinator>(),
            dialogService ?? Substitute.For<IDialogService>(),
            new InlineUiThreadDispatcher(), TestAppEnvironment.Create(UiLayoutProfile.Workspace).Input with { HasPointer = hasPointer }, new InlineBackgroundTaskRunner(), timeProvider: new FakeTimeProvider());
        return editor;
    }

    private static BikeSnapshot CreateSnapshot()
    {
        var bike = new Bike(Guid.NewGuid(), "view bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = 0.5,
            PixelsToMillimeters = 1,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true)),
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = TestSnapshots.WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            ImageRotationDegrees = 12.5,
            ImageBytes = TestImages.SmallPngBytes(),
            Updated = 1,
        };

        return BikeSnapshot.From(bike);
    }

    private static void EnsureBikeEditorViewResources()
    {
        ViewTestHelpers.EnsureViewTestResources();
    }
}
