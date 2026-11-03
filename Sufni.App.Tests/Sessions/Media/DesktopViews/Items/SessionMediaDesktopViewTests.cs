using Sufni.App.Sessions.Detail.DesktopViews.Controls;
using Sufni.App.Sessions.Models;
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
        using var video = await CreateVideoPaneAsync();
        using var workspace = CreateWorkspace(video);

        await using var mounted = await MountAsync(workspace);

        Assert.Single(mounted.View.GetVisualDescendants().OfType<MapView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<VideoPaneView>());
        Assert.True(FindSplit(mounted.View).HasSecondPane);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_OmitsVideoPane_WhenWorkspaceHasNoVideo()
    {
        using var workspace = CreateWorkspace(video: null);

        await using var mounted = await MountAsync(workspace);

        Assert.Empty(mounted.View.GetVisualDescendants().OfType<VideoPaneView>());
        Assert.False(FindSplit(mounted.View).HasSecondPane);
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

    private static async Task<MountedView<SessionMediaDesktopView>> MountAsync(
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
        return new MountedView<SessionMediaDesktopView>(host, view);
    }

    private static CollapsibleSplitView FindSplit(SessionMediaDesktopView view) =>
        view.FindControl<CollapsibleSplitView>("MediaSplit")!;

    public void Dispose() => videoFixture.Dispose();
}
