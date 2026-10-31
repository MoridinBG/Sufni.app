using Sufni.App.Video.Models;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Views;
using Sufni.App.Sessions.Media.DesktopViews.Items;
using Sufni.App.Shared.DesktopViews.Controls;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.ViewModels;
using Sufni.App.Video.Views;

namespace Sufni.App.Tests.Sessions.Media.DesktopViews.Items;

[Collection("Ui")]
public class SessionMediaDesktopViewTests : IDisposable
{
    private readonly VideoFixture videoFixture = new();

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_ShowsMapAndVideoPanes_WhenWorkspaceHasVideo()
    {
        var workspace = CreateWorkspace(await CreateVideoPaneAsync());

        await using var mounted = await MountAsync(workspace);

        Assert.Single(mounted.View.GetVisualDescendants().OfType<MapView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<VideoPaneView>());
        Assert.True(FindSplit(mounted.View).HasSecondPane);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_OmitsVideoPane_WhenWorkspaceHasNoVideo()
    {
        var workspace = CreateWorkspace(video: null);

        await using var mounted = await MountAsync(workspace);

        Assert.Empty(mounted.View.GetVisualDescendants().OfType<VideoPaneView>());
        Assert.False(FindSplit(mounted.View).HasSecondPane);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_SplitCommit_PublishesMapAndVideoRatios()
    {
        var workspace = CreateWorkspace(await CreateVideoPaneAsync());

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.5),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Video, 0.5),
            ]));

        var split = FindSplit(mounted.View);
        split.BeginDragForTests();
        split.DragToFirstRatioForTests(0.4);
        split.CompleteDragForTests();
        await ViewTestHelpers.FlushDispatcherAsync();

        var panes = mounted.View.LayoutPreferences!.Panes;
        Assert.Equal([SessionLayoutPaneIds.Map, SessionLayoutPaneIds.Video], panes.Select(pane => pane.PaneId).ToArray());
        Assert.Equal(0.4, panes[0].Ratio, precision: 6);
        Assert.Equal(0.6, panes[1].Ratio, precision: 6);
    }

    private Task<VideoPaneViewModel> CreateVideoPaneAsync()
    {
        return videoFixture.CreatePaneAsync(
            new TestRecordedSessionTimeline(),
            Observable.Return(new VideoSessionTiming("source-hash", 0, 60)),
            videoFixture.CreateClip("source-hash", startTimestamp: 1_000, durationSeconds: 60));
    }

    private static TestSessionMediaWorkspace CreateWorkspace(VideoPaneViewModel? video) =>
        new(trackPoints: [new TrackPoint(1, 2, 3, 4)], video: video);

    private static async Task<MountedSessionMediaDesktopView> MountAsync(
        TestSessionMediaWorkspace workspace,
        SessionPaneGroupPreferences? layoutPreferences = null)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var view = new SessionMediaDesktopView
        {
            DataContext = workspace,
            LayoutPreferences = layoutPreferences,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionMediaDesktopView(host, view);
    }

    private static CollapsibleSplitView FindSplit(SessionMediaDesktopView view) =>
        view.FindControl<CollapsibleSplitView>("MediaSplit")!;

    public void Dispose() => videoFixture.Dispose();
}

internal sealed class MountedSessionMediaDesktopView(Window host, SessionMediaDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionMediaDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
