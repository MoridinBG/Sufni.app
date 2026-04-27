using System;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Presentation;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.App.Views.SessionPages;

namespace Sufni.App.Tests.Views.SessionPages;

public class LiveGraphPageViewTests
{
    [AvaloniaFact]
    public async Task LiveGraphPageView_BindsPlaceholderContainers_ToWorkspaceState()
    {
        var workspace = Substitute.For<ILiveSessionGraphWorkspace>();
        workspace.GraphBatches.Returns(new Subject<LiveGraphBatch>());
        workspace.PlotRanges.Returns(new LiveSessionPlotRanges(TravelMaximum: 180, VelocityMaximum: 5, ImuMaximum: 5));
        workspace.Timeline.Returns(new SessionTimelineLinkViewModel());
        workspace.TravelGraphState.Returns(SurfacePresentationState.Ready);
        workspace.ImuGraphState.Returns(SurfacePresentationState.Hidden);

        var page = new LiveGraphPageViewModel(workspace);

        await using var mounted = await MountAsync(page);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        Assert.Equal(2, hosts.Length);
        Assert.Equal(SurfacePresentationState.Ready, hosts[0].PresentationState);
        Assert.Equal(SurfacePresentationState.Hidden, hosts[1].PresentationState);
    }

    [AvaloniaFact]
    public async Task LiveGraphPageView_ExposesWorkspace_AsDataContextPath()
    {
        var workspace = Substitute.For<ILiveSessionGraphWorkspace>();
        workspace.GraphBatches.Returns(new Subject<LiveGraphBatch>());
        workspace.PlotRanges.Returns(new LiveSessionPlotRanges(TravelMaximum: 180, VelocityMaximum: 5, ImuMaximum: 5));
        workspace.Timeline.Returns(new SessionTimelineLinkViewModel());
        workspace.TravelGraphState.Returns(SurfacePresentationState.Hidden);
        workspace.ImuGraphState.Returns(SurfacePresentationState.Hidden);

        var page = new LiveGraphPageViewModel(workspace);

        await using var mounted = await MountAsync(page);

        Assert.Same(workspace, page.Workspace);
        Assert.Same(page, mounted.View.DataContext);
    }

    private static async Task<MountedLiveGraphPageView> MountAsync(LiveGraphPageViewModel page)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new LiveGraphPageView
        {
            DataContext = page,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedLiveGraphPageView(host, view);
    }
}

internal sealed record MountedLiveGraphPageView(Window Host, LiveGraphPageView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
