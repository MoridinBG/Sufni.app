using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Sessions.Detail.DesktopViews.Editors;
using Sufni.App.Shared.DesktopViews.Controls;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Sessions.Detail.DesktopViews.Editors;

[Collection("Ui")]
public class SessionDetailDesktopShellTests
{
    [AvaloniaFact]
    public async Task SessionShellDesktopView_ComposesOptionalRegions()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var signals = new Border();
        var media = new Border();
        var analysis = new Border();
        var controls = new Border();
        var sidebar = new Border();
        var shell = new SessionShellDesktopView
        {
            SignalsContent = signals,
            HasMediaContent = true,
            MediaContent = media,
            AnalysisContent = analysis,
            ControlContent = controls,
            SidebarContent = sidebar,
        };

        await using var mounted = await MountAsync(shell);

        Assert.Same(signals, mounted.View.FindControl<ContentControl>("SignalsHost")!.Content);
        Assert.Same(media, mounted.View.FindControl<ContentControl>("MediaHost")!.Content);
        Assert.Same(analysis, mounted.View.FindControl<ContentControl>("AnalysisHost")!.Content);
        Assert.Same(controls, mounted.View.FindControl<ContentControl>("ControlHost")!.Content);
        Assert.Same(sidebar, mounted.View.FindControl<ContentControl>("SidebarHost")!.Content);
        Assert.True(mounted.View.FindControl<CollapsibleSplitView>("SignalsMediaSplit")!.HasSecondPane);
        Assert.True(mounted.View.FindControl<GridSplitter>("ControlSplitter")!.IsVisible);

        shell.HasMediaContent = false;
        shell.ControlContent = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.View.FindControl<CollapsibleSplitView>("SignalsMediaSplit")!.HasSecondPane);
        Assert.Null(mounted.View.FindControl<ContentControl>("ControlHost")!.Content);
        Assert.False(mounted.View.FindControl<GridSplitter>("ControlSplitter")!.IsVisible);
    }

    private static async Task<MountedSessionShellDesktopView> MountAsync(SessionShellDesktopView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionShellDesktopView(host, view);
    }
}

internal sealed class MountedSessionShellDesktopView(Window host, SessionShellDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionShellDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
