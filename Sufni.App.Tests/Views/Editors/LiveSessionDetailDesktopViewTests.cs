using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class LiveSessionDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionDetailDesktopView_RendersPlaceholderShellContent()
    {
        var editor = new LiveSessionDetailViewModel
        {
            Name = "Live Session 01"
        };
        editor.ControlState = LiveSessionControlState.Empty with
        {
            ConnectionState = LiveConnectionState.Connected,
            SessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 909),
        };

        await using var mounted = await MountAsync(editor);

        Assert.NotNull(mounted.View.FindControl<TextBlock>("TravelGraphPlaceholderTextBlock"));
        Assert.Equal("State: Connected", mounted.View.FindControl<TextBlock>("LiveConnectionStateTextBlock")!.Text);
        Assert.Equal("Session: 909", mounted.View.FindControl<TextBlock>("LiveSessionIdTextBlock")!.Text);
        Assert.Equal(
            "Live statistics will appear once the per-tab live session service is wired in.",
            mounted.View.FindControl<TextBlock>("StatisticsPlaceholderTextBlock")!.Text);
    }

    private static async Task<MountedLiveSessionDetailDesktopView> MountAsync(LiveSessionDetailViewModel editor)
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        var view = new LiveSessionDetailDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedLiveSessionDetailDesktopView(host, view);
    }
}

internal sealed class MountedLiveSessionDetailDesktopView(Window host, LiveSessionDetailDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveSessionDetailDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}