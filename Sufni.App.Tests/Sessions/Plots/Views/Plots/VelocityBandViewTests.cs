using Sufni.App.Sessions.Presentation;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.Views.SessionPages;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.LiveDaq;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Sufni.App.Shared.Views;
using Sufni.App.Tests.TestSupport.Async;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.Telemetry;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Common;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Shell.Behaviors;
using Sufni.App.Shared.Views.Input;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Plots.Views.Plots;

[Collection("Ui")]
public class VelocityBandViewTests
{
    [AvaloniaFact]
    public async Task LiveCutoffDrag_KeepsTheDampingSurfaceVisibleUntilRelease()
    {
        EnsureVelocityBandStyle();
        ViewTestHelpers.EnsurePlotViewStyle();
        var context = LiveCaptureTestData.Context();
        var bike = TestSnapshots.Bike(context.BikeId);
        using var bikes = new BikeStoreStub(bike);
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, Arg.Any<double>(),
                SuspensionType.Front, DampingSpeedCircuit.Compression, Arg.Any<double>())
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Saved(bike));
        using var workspace = new LiveSessionAnalysisWorkspaceViewModel(context,
            Substitute.For<ILiveCaptureCoordinator>(), bikes, bikeCoordinator, _ => { });
        workspace.Apply(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed()));
        var view = new DampingPageView { DataContext = new DampingPageViewModel(workspace), Width = 800, Height = 900 };
        PointerGesture.SetSupportsTouchLongPressContextMenu(view, false);
        await using var mounted = new MountedView<DampingPageView>(await ViewTestHelpers.ShowViewAsync(view), view);
        var dampingHost = view.FindControl<DampingAnalysisHost>("FrontDampingAnalysisHost")!;
        var band = dampingHost.FindControl<VelocityBandView>("VelocityBand")!;
        var plot = Assert.Single(dampingHost.GetVisualDescendants().OfType<AnalysisPlotView>());
        var start = Translate(FindHandle(band, "PART_CompressionHandle"), mounted.Host);
        var firstTarget = start + new Vector(0, 25);
        var secondTarget = start + new Vector(0, 50);
        Assert.True(plot.IsEffectivelyVisible);

        mounted.Host.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        try
        {
            mounted.Host.MouseMove(firstTarget, RawInputModifiers.LeftMouseButton);
            await ViewTestHelpers.FlushDispatcherAsync();
            Assert.True(band.IsEffectivelyVisible);
            Assert.True(plot.IsEffectivelyVisible);
            Assert.True(band.IsDampingGuideVisible);
            var firstCutoff = workspace.DampingSpeedCutoffs.Front.CompressionMmPerSecond;
            Assert.NotEqual(bike.DampingSpeedCutoffs.Front.CompressionMmPerSecond, firstCutoff);

            mounted.Host.MouseMove(secondTarget, RawInputModifiers.LeftMouseButton);
            await ViewTestHelpers.FlushDispatcherAsync();
            Assert.True(band.IsDampingGuideVisible);
            Assert.NotEqual(firstCutoff, workspace.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        }
        finally { mounted.Host.MouseUp(secondTarget, MouseButton.Left, RawInputModifiers.None); }
        await ViewTestHelpers.FlushDispatcherAsync();

        await bikeCoordinator.Received(1).UpdateDampingSpeedCutoffAsync(bike.Id,
            bike.DampingSpeedCutoffs.Front.CompressionMmPerSecond,
            SuspensionType.Front, DampingSpeedCircuit.Compression, Arg.Any<double>());
    }

    [AvaloniaFact]
    public async Task DesktopDrag_PreviewsCommitsAndDoesNotRequestHapticFeedback()
    {
        var workspace = CreateWorkspace();
        var view = CreateMountedView(workspace);
        var hapticCount = 0;
        view.AddHandler(
            HapticFeedbackBehavior.LongPressFeedbackRequestedEvent,
            (_, _) => hapticCount++);

        await using var mounted = await MountAsync(view);
        var handle = FindHandle(view, "PART_CompressionHandle");
        var start = Translate(handle, mounted.Host);
        var dragTarget = Translate(view, mounted.Host, new Point(view.Bounds.Width / 2.0, 40 + (420 - 95) * 0.75));

        mounted.Host.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseMove(dragTarget, RawInputModifiers.LeftMouseButton);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(view.IsDampingGuideVisible);
        await workspace.DidNotReceive().CommitDampingSpeedCutoffAsync(
            Arg.Any<SuspensionType>(), Arg.Any<DampingSpeedCircuit>(), Arg.Any<double>());
        mounted.Host.MouseUp(dragTarget, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        workspace.Received().PreviewDampingSpeedCutoff(
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            Arg.Is<double>(cutoff => Math.Abs(cutoff - 1000) < 0.01));
        await workspace.Received().CommitDampingSpeedCutoffAsync(
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            Arg.Is<double>(cutoff => Math.Abs(cutoff - 1000) < 0.01));
        Assert.Equal(0, hapticCount);
        Assert.False(view.IsDampingGuideVisible);
    }

    [AvaloniaFact]
    public async Task MobileMovementBeforeLongPress_CancelsActivation()
    {
        using var scheduler = new ManualUiTimerScheduler();
        var workspace = CreateWorkspace();
        var view = CreateMountedView(workspace);
        PointerGesture.SetSupportsTouchLongPressContextMenu(view, true);
        var hapticCount = 0;
        view.AddHandler(
            HapticFeedbackBehavior.LongPressFeedbackRequestedEvent,
            (_, _) => hapticCount++);

        await using var mounted = await MountAsync(view, scheduler);
        var handle = FindHandle(view, "PART_ReboundHandle");
        var start = Translate(handle, mounted.Host);
        var moved = start + new Vector(0, 12);

        mounted.Host.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseMove(moved, RawInputModifiers.LeftMouseButton);
        using var tick = scheduler.ScheduleOnce(TimeSpan.FromSeconds(1), () => { });
        scheduler.FireAll();
        await ViewTestHelpers.FlushDispatcherAsync();
        mounted.Host.MouseUp(moved, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        workspace.DidNotReceive().PreviewDampingSpeedCutoff(
            Arg.Any<SuspensionType>(),
            Arg.Any<DampingSpeedCircuit>(),
            Arg.Any<double>());
        await workspace.DidNotReceive().CommitDampingSpeedCutoffAsync(
            Arg.Any<SuspensionType>(),
            Arg.Any<DampingSpeedCircuit>(),
            Arg.Any<double>());
        Assert.Equal(0, hapticCount);
    }

    [AvaloniaFact]
    public async Task MobileLongPress_RequestsHapticFeedbackThenPreviewsAndCommits()
    {
        using var scheduler = new ManualUiTimerScheduler();
        var workspace = CreateWorkspace();
        var view = CreateMountedView(workspace);
        PointerGesture.SetSupportsTouchLongPressContextMenu(view, true);
        var hapticCount = 0;
        view.AddHandler(
            HapticFeedbackBehavior.LongPressFeedbackRequestedEvent,
            (_, _) => hapticCount++);

        await using var mounted = await MountAsync(view, scheduler);
        var handle = FindHandle(view, "PART_ReboundHandle");
        var start = Translate(handle, mounted.Host);
        var dragTarget = Translate(view, mounted.Host, new Point(view.Bounds.Width / 2.0, 40 + (420 - 95) * 0.25));

        mounted.Host.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        scheduler.FireAll();
        await ViewTestHelpers.FlushDispatcherAsync();
        mounted.Host.MouseMove(dragTarget, RawInputModifiers.LeftMouseButton);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(view.IsDampingGuideVisible);
        await workspace.DidNotReceive().CommitDampingSpeedCutoffAsync(
            Arg.Any<SuspensionType>(), Arg.Any<DampingSpeedCircuit>(), Arg.Any<double>());
        mounted.Host.MouseUp(dragTarget, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, hapticCount);
        workspace.Received().PreviewDampingSpeedCutoff(
            SuspensionType.Front,
            DampingSpeedCircuit.Rebound,
            Arg.Is<double>(cutoff => Math.Abs(cutoff - 1000) < 0.01));
        await workspace.Received().CommitDampingSpeedCutoffAsync(
            SuspensionType.Front,
            DampingSpeedCircuit.Rebound,
            Arg.Is<double>(cutoff => Math.Abs(cutoff - 1000) < 0.01));
    }

    private static VelocityBandView CreateMountedView(ISessionAnalysisWorkspace workspace) => new()
    {
        Width = 80,
        Height = 420,
        SuspensionType = SuspensionType.Front,
        DampingSpeedCutoffs = DampingSpeedCutoffs.Default,
        CanEditDampingSpeedCutoffs = true,
        AnalysisWorkspace = workspace,
    };

    private static ISessionAnalysisWorkspace CreateWorkspace()
    {
        var workspace = Substitute.For<ISessionAnalysisWorkspace>();
        workspace.CommitDampingSpeedCutoffAsync(
                Arg.Any<SuspensionType>(),
                Arg.Any<DampingSpeedCircuit>(),
                Arg.Any<double>())
            .Returns(Task.CompletedTask);
        return workspace;
    }

    private static async Task<MountedView<VelocityBandView>> MountAsync(VelocityBandView view, ManualUiTimerScheduler? scheduler = null)
    {
        EnsureVelocityBandStyle();
        var host = await ViewTestHelpers.ShowViewAsync(view);
        if (scheduler is not null) UiTimers.SetScheduler(host, scheduler);
        return new MountedView<VelocityBandView>(host, view);
    }

    private static void EnsureVelocityBandStyle()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var source = new Uri("avares://Sufni.App/Sessions/Plots/Views/Plots/VelocityBandView.axaml");
        if (application.Styles.OfType<StyleInclude>().Any(style => style.Source?.AbsoluteUri == source.AbsoluteUri))
        {
            return;
        }

        application.Styles.Add(new StyleInclude(new Uri("avares://Sufni.App/"))
        {
            Source = source
        });
    }

    private static Control FindHandle(VelocityBandView view, string name)
    {
        return view.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name);
    }

    private static Point Translate(Control control, Window host, Point? point = null)
    {
        var translated = control.TranslatePoint(
            point ?? new Point(control.Bounds.Width / 2.0, control.Bounds.Height / 2.0),
            host);
        Assert.NotNull(translated);
        return translated.Value;
    }

}
