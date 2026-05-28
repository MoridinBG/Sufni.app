using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Behaviors;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.Plots;

public class VelocityBandViewTests
{
    [Fact]
    public void ZoneLengths_AlignToVelocityThresholds()
    {
        var sut = new VelocityBandView();

        Assert.Equal(GridUnitType.Star, sut.HighSpeedZoneLength.GridUnitType);
        Assert.Equal(GridUnitType.Star, sut.LowSpeedZoneLength.GridUnitType);
        Assert.Equal(
            SessionDampingSettings.VelocityHistogramLimitMmPerSecond -
            SessionDampingSettings.HighSpeedThresholdMmPerSecond,
            sut.HighSpeedZoneLength.Value);
        Assert.Equal(
            SessionDampingSettings.HighSpeedThresholdMmPerSecond,
            sut.LowSpeedZoneLength.Value);
    }

    [Fact]
    public void ZoneLengths_ReflectCompressionAndReboundCutoffsIndependently()
    {
        var cutoffs = DampingSpeedCutoffs.FromValues(
            frontCompressionMmPerSecond: 300,
            frontReboundMmPerSecond: 500,
            rearCompressionMmPerSecond: 700,
            rearReboundMmPerSecond: 900);
        var sut = new VelocityBandView
        {
            SuspensionType = SuspensionType.Rear,
            DampingSpeedCutoffs = cutoffs,
        };

        Assert.Equal(1100, sut.HsrZoneLength.Value);
        Assert.Equal(900, sut.LsrZoneLength.Value);
        Assert.Equal(700, sut.LscZoneLength.Value);
        Assert.Equal(1300, sut.HscZoneLength.Value);
        Assert.Equal(900, sut.ReboundCutoff);
        Assert.Equal(700, sut.CompressionCutoff);
    }

    [AvaloniaFact]
    public async Task VelocityBandView_BandsUseVelocityHistogramDataAreaOffsets()
    {
        TestApp.SetIsDesktop(true);
        var view = CreateMountedView(CreateWorkspace());

        await using var mounted = await MountAsync(view);
        var bandsGrid = view.GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Name == "PART_BandsGrid");

        Assert.InRange(Math.Abs(bandsGrid.Bounds.Y - 40), 0, 0.5);
        Assert.InRange(Math.Abs(bandsGrid.Bounds.Height - (view.Bounds.Height - 95)), 0, 0.5);
    }

    [AvaloniaFact]
    public async Task DesktopDrag_PreviewsCommitsAndDoesNotRequestHapticFeedback()
    {
        TestApp.SetIsDesktop(true);
        var workspace = CreateWorkspace();
        var view = CreateMountedView(workspace);
        var hapticCount = 0;
        view.AddHandler(
            HapticFeedbackBehavior.LongPressFeedbackRequestedEvent,
            (_, _) => hapticCount++);

        await using var mounted = await MountAsync(view);
        var handle = FindHandle(view, "PART_CompressionHandle");
        var start = Translate(handle, mounted.Host);
        var dragTarget = Translate(view, mounted.Host, new Point(view.Bounds.Width / 2.0, view.Bounds.Height - 70));

        mounted.Host.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseMove(dragTarget, RawInputModifiers.LeftMouseButton);
        mounted.Host.MouseUp(dragTarget, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        workspace.Received().PreviewDampingSpeedCutoff(
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            Arg.Any<double>());
        await workspace.Received().CommitDampingSpeedCutoffAsync(
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            Arg.Any<double>());
        Assert.Equal(0, hapticCount);
        Assert.False(view.IsDampingGuideVisible);
        Assert.Equal(string.Empty, view.DampingGuideLabel);
    }

    [AvaloniaFact]
    public async Task MobileMovementBeforeLongPress_CancelsActivation()
    {
        TestApp.SetIsDesktop(false);
        try
        {
            var workspace = CreateWorkspace();
            var view = CreateMountedView(workspace);
            var hapticCount = 0;
            view.AddHandler(
                HapticFeedbackBehavior.LongPressFeedbackRequestedEvent,
                (_, _) => hapticCount++);

            await using var mounted = await MountAsync(view);
            var handle = FindHandle(view, "PART_ReboundHandle");
            var start = Translate(handle, mounted.Host);
            var moved = start + new Vector(0, 12);

            mounted.Host.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            mounted.Host.MouseMove(moved, RawInputModifiers.LeftMouseButton);
            await Task.Delay(DampingSpeedCutoffs.MobileLongPressDelay + TimeSpan.FromMilliseconds(50));
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
        finally
        {
            TestApp.SetIsDesktop(true);
        }
    }

    [AvaloniaFact]
    public async Task MobileLongPress_RequestsHapticFeedbackThenPreviewsAndCommits()
    {
        TestApp.SetIsDesktop(false);
        try
        {
            var workspace = CreateWorkspace();
            var view = CreateMountedView(workspace);
            var hapticCount = 0;
            view.AddHandler(
                HapticFeedbackBehavior.LongPressFeedbackRequestedEvent,
                (_, _) => hapticCount++);

            await using var mounted = await MountAsync(view);
            var handle = FindHandle(view, "PART_ReboundHandle");
            var start = Translate(handle, mounted.Host);
            var dragTarget = Translate(view, mounted.Host, new Point(view.Bounds.Width / 2.0, 120));

            mounted.Host.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            await Task.Delay(DampingSpeedCutoffs.MobileLongPressDelay + TimeSpan.FromMilliseconds(50));
            await ViewTestHelpers.FlushDispatcherAsync();
            mounted.Host.MouseMove(dragTarget, RawInputModifiers.LeftMouseButton);
            mounted.Host.MouseUp(dragTarget, MouseButton.Left, RawInputModifiers.None);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(1, hapticCount);
            workspace.Received().PreviewDampingSpeedCutoff(
                SuspensionType.Front,
                DampingSpeedCircuit.Rebound,
                Arg.Any<double>());
            await workspace.Received().CommitDampingSpeedCutoffAsync(
                SuspensionType.Front,
                DampingSpeedCircuit.Rebound,
                Arg.Any<double>());
        }
        finally
        {
            TestApp.SetIsDesktop(true);
        }
    }

    [AvaloniaFact]
    public async Task VelocityStatisticsHost_DashedGuideIsClippedToVelocityGraphColumn()
    {
        TestApp.SetIsDesktop(true);
        var workspace = CreateWorkspace();
        var host = new VelocityStatisticsHost
        {
            Width = 600,
            Height = 420,
            PresentationState = SurfacePresentationState.Ready,
            HasDynamicStatistics = false,
            StaticSource = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"12\" />",
            SuspensionType = SuspensionType.Front,
            DampingSpeedCutoffs = DampingSpeedCutoffs.Default,
            StatisticsWorkspace = workspace,
        };

        EnsureVelocityBandStyle();
        var window = await ViewTestHelpers.ShowViewAsync(host);
        try
        {
            var band = host.GetVisualDescendants().OfType<VelocityBandView>().Single();
            var guide = host.GetVisualDescendants().OfType<Line>().Single();
            var handle = FindHandle(band, "PART_ReboundHandle");
            var start = Translate(handle, window);
            var dragTarget = Translate(band, window, new Point(band.Bounds.Width / 2.0, 120));

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(dragTarget, RawInputModifiers.LeftMouseButton);
            await ViewTestHelpers.FlushDispatcherAsync();

            var guideLayer = Assert.IsType<Canvas>(guide.Parent);
            var guideLabel = host.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_DampingGuideLabel");
            var guideLabelText = guideLabel.GetVisualDescendants().OfType<TextBlock>().Single();

            Assert.True(band.IsDampingGuideVisible);
            Assert.True(guide.IsVisible);
            Assert.True(guideLabel.IsVisible);
            Assert.StartsWith("-", guideLabelText.Text ?? string.Empty);
            Assert.EndsWith(" mm/s", guideLabelText.Text ?? string.Empty);
            Assert.Equal(0, Grid.GetColumn(guideLayer));
            Assert.Equal(1, Grid.GetColumnSpan(guideLayer));
            Assert.Equal(0, guide.StartPoint.X);
            Assert.True(guide.EndPoint.X > host.Bounds.Width);
            Assert.Equal(guide.StartPoint.Y, guide.EndPoint.Y);

            window.MouseUp(dragTarget, MouseButton.Left, RawInputModifiers.None);
            await ViewTestHelpers.FlushDispatcherAsync();
        }
        finally
        {
            window.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private static VelocityBandView CreateMountedView(ISessionStatisticsWorkspace workspace) => new()
    {
        Width = 80,
        Height = 420,
        SuspensionType = SuspensionType.Front,
        DampingSpeedCutoffs = DampingSpeedCutoffs.Default,
        CanEditDampingSpeedCutoffs = true,
        StatisticsWorkspace = workspace,
    };

    private static ISessionStatisticsWorkspace CreateWorkspace()
    {
        var workspace = Substitute.For<ISessionStatisticsWorkspace>();
        workspace.CanEditDampingSpeedCutoffs.Returns(true);
        workspace.DampingSpeedCutoffs.Returns(DampingSpeedCutoffs.Default);
        workspace.DamperPercentages.Returns(SessionDamperPercentages.Empty);
        workspace.FrontStatisticsState.Returns(SurfacePresentationState.Ready);
        workspace.RearStatisticsState.Returns(SurfacePresentationState.Ready);
        workspace.CompressionBalanceState.Returns(SurfacePresentationState.Ready);
        workspace.ReboundBalanceState.Returns(SurfacePresentationState.Ready);
        workspace.CommitDampingSpeedCutoffAsync(
                Arg.Any<SuspensionType>(),
                Arg.Any<DampingSpeedCircuit>(),
                Arg.Any<double>())
            .Returns(Task.CompletedTask);
        return workspace;
    }

    private static async Task<MountedVelocityBandView> MountAsync(VelocityBandView view)
    {
        EnsureVelocityBandStyle();
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedVelocityBandView(host);
    }

    private static void EnsureVelocityBandStyle()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var source = new Uri("avares://Sufni.App/Views/Plots/VelocityBandView.axaml");
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

    private sealed class MountedVelocityBandView(Window host) : IAsyncDisposable
    {
        public Window Host { get; } = host;

        public async ValueTask DisposeAsync()
        {
            Host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
