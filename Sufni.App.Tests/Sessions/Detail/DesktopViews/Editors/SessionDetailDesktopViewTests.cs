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
namespace Sufni.App.Tests.Sessions.Detail.DesktopViews.Editors;

[Collection("Ui")]
public class SessionDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ComposesRecordedSessionRegionsIntoShellHosts()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: context.CreateLoadedState(includeImu: true));

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();
        var signalsHost = shell.FindControl<ContentControl>("SignalsHost");
        var mediaHost = shell.FindControl<ContentControl>("MediaHost");
        var analysisHost = shell.FindControl<ContentControl>("AnalysisHost");
        var controlHost = shell.FindControl<ContentControl>("ControlHost");
        var sidebarHost = shell.FindControl<ContentControl>("SidebarHost");
        var errorMessagesBar = mounted.View.FindControl<ErrorMessagesBar>("SessionErrorMessagesBar");

        var signalsView = Assert.IsType<RecordedSessionSignalsDesktopView>(shell.SignalsContent);
        var mediaView = Assert.IsType<SessionMediaDesktopView>(shell.MediaContent);
        var analysisView = Assert.IsType<SessionAnalysisDesktopView>(shell.AnalysisContent);
        var sidebarView = Assert.IsType<SessionSidebarDesktopView>(shell.SidebarContent);

        Assert.NotNull(signalsHost);
        Assert.NotNull(mediaHost);
        Assert.NotNull(analysisHost);
        Assert.NotNull(controlHost);
        Assert.NotNull(sidebarHost);
        Assert.NotNull(errorMessagesBar);

        Assert.Same(signalsView, signalsHost!.Content);
        Assert.Same(mediaView, mediaHost!.Content);
        Assert.Same(analysisView, analysisHost!.Content);
        Assert.Null(controlHost!.Content);
        Assert.Same(sidebarView, sidebarHost!.Content);

        Assert.Same(mounted.Editor.SignalsWorkspace, signalsView.DataContext);
        Assert.Same(mounted.Editor.MediaWorkspace, mediaView.DataContext);
        Assert.Same(mounted.Editor.AnalysisWorkspace, analysisView.DataContext);
        Assert.Same(mounted.Editor.SidebarWorkspace, sidebarView.DataContext);
        Assert.Same(mounted.Editor, errorMessagesBar!.DataContext);
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ReplacesShellWithIncompleteState_WhenLocalDataIsMissing()
    {
        var context = new SessionDetailViewTestContext();
        var snapshot = context.CreateTelemetryBearingSnapshot(hasProcessedData: true);

        await using var mounted = await context.MountDesktopAsync(
            snapshot: snapshot,
            loadResult: new SessionDetailLoadResult.IncompleteLocalData(
                snapshot.Id,
                new MissingSessionData(
                    ProcessedTelemetryBlob: true,
                    RecordedSourceMissingOrHashMismatch: false)));

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();
        var incompleteText = mounted.View.FindControl<TextBlock>("ScreenIncompleteText");

        Assert.NotNull(incompleteText);
        Assert.Contains("processed telemetry", incompleteText!.Text);
        Assert.False(shell.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ReplacesShellWithScreenError_WhenDesktopLoadFails()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: new SessionDetailLoadResult.Failed("boom"));

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();
        var errorText = mounted.View.FindControl<TextBlock>("ScreenErrorText");

        Assert.NotNull(errorText);
        Assert.False(shell.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ShowsProgressOnlyLoadingOverlay_WhileLoadIsPending()
    {
        var context = new SessionDetailViewTestContext();
        var loadCompletion = new TaskCompletionSource<SessionDetailLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var mounted = await context.MountDesktopAsync(loadTask: loadCompletion.Task);

        var busyOverlay = mounted.View.FindControl<BusyOverlay>("ScreenBusyOverlay")
            ?? throw new InvalidOperationException("Screen busy overlay was not found.");

        Assert.True(busyOverlay.IsActive);
        Assert.True(busyOverlay.IsVisible);
        Assert.True(busyOverlay.ShowProgress);
        Assert.False(busyOverlay.ShowIndicator);
        Assert.Equal(SessionDetailLoadProgress.PreparingSession.Message, busyOverlay.Message);
        Assert.NotNull(busyOverlay.MessageForeground);
        Assert.Equal(SessionDetailLoadProgress.PreparingSession.ProgressFraction, busyOverlay.ProgressValue);

        loadCompletion.SetResult(context.CreateLoadedState());
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_TakesKeyboardFocus_WhenLoaded()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: context.CreateLoadedState());

        Assert.True(mounted.View.IsFocused);
    }
}
