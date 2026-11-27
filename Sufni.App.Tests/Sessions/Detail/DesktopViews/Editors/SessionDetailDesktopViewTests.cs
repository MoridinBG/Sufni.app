using Sufni.App.Shared.Models;
using Sufni.App.Sessions.Presentation.Views;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Sufni.App.Sessions.Detail.DesktopViews.Editors;
using Sufni.App.Sessions.Detail.DesktopViews.Items;
using Sufni.App.Sessions.Signals.DesktopViews.Items;
using Sufni.App.Sessions.Media.DesktopViews.Items;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Analysis.DesktopViews.Items;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.Sessions.Detail.Views.Editors;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;
namespace Sufni.App.Tests.Sessions.Detail.DesktopViews.Editors;

[Collection("Ui")]
public class SessionDetailDesktopViewTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionDetailDesktopView_ShowsSensorFindingsPanel_OnlyWhenTheLoadedGenerationHasFindings(bool hasFindings)
    {
        var context = new SessionDetailViewTestContext();
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        SensorFinding[] findings = hasFindings ? [new(new SensorIdentity(SensorKind.ShockTravel), SensorFaultReason.Frozen, [new(1, 3)])] : [];

        await using var mounted = await context.MountDesktopAsync(snapshot,
            context.CreateLoadedState(TestTelemetryData.CreateProcessed() with { SensorFindings = [.. findings] }),
            domain: SessionDetailHarness.DomainFromSnapshot(snapshot));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(hasFindings, mounted.View.FindNamedVisual<Panel>("SensorFindingsPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ReplacesShellWithIncompleteState_WhenLocalDataIsMissing()
    {
        var context = new SessionDetailViewTestContext();
        var snapshot = TestSnapshots.Session(hasProcessedData: true);

        await using var mounted = await context.MountDesktopAsync(
            snapshot: snapshot,
            loadResult: new SessionDetailLoadResult.IncompleteLocalData(
                snapshot.Id,
                new MissingSessionData(
                    ProcessedTelemetryBlob: true,
                    RecordedSourceMissing: false)));

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();
        var incompleteText = mounted.View.FindControl<SessionScreenStateOverlay>("ScreenOverlay")!.FindControl<TextBlock>("ScreenIncompleteText");

        Assert.NotNull(incompleteText);
        Assert.True(incompleteText!.IsEffectivelyVisible);
        Assert.False(string.IsNullOrWhiteSpace(incompleteText.Text));
        Assert.False(shell.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ReplacesShellWithScreenError_WhenDesktopLoadFails()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: new SessionDetailLoadResult.Failed(SessionLoadFailureReason.Read, "boom"));

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();
        var errorText = mounted.View.FindControl<SessionScreenStateOverlay>("ScreenOverlay")!.FindControl<TextBlock>("ScreenErrorText");

        Assert.NotNull(errorText);
        Assert.True(errorText!.IsEffectivelyVisible);
        Assert.False(string.IsNullOrWhiteSpace(errorText.Text));
        Assert.DoesNotContain("boom", errorText.Text);
        Assert.False(shell.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ShowsProgressOnlyLoadingOverlay_WhileLoadIsPending()
    {
        var context = new SessionDetailViewTestContext();
        var loadCompletion = new TaskCompletionSource<SessionDetailLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var mounted = await context.MountDesktopAsync(
            snapshot: TestSnapshots.Session(hasProcessedData: true),
            loadTask: loadCompletion.Task);

        var busyOverlay = mounted.View.FindControl<SessionScreenStateOverlay>("ScreenOverlay")!.FindControl<BusyOverlay>("ScreenBusyOverlay")
            ?? throw new InvalidOperationException("Screen busy overlay was not found.");

        Assert.True(busyOverlay.IsActive);
        Assert.True(busyOverlay.IsVisible);
        Assert.True(busyOverlay.ShowProgress);
        Assert.False(busyOverlay.ShowIndicator);
        Assert.InRange(busyOverlay.ProgressValue, 0, 1);

        var load = mounted.Editor.LoadedCommand.ExecutionTask!;
        loadCompletion.SetResult(context.CreateLoadedState());
        await load.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(busyOverlay.IsActive);
        Assert.False(busyOverlay.IsVisible);
        Assert.True(mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single().IsEffectivelyVisible);
    }

}
