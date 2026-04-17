using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class LiveSessionDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionDetailDesktopView_RendersLiveShellContent()
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

        var textBlocks = mounted.View.GetVisualDescendants().OfType<TextBlock>().ToArray();
        var controls = mounted.View.GetVisualDescendants().OfType<Control>().ToArray();

        Assert.Equal("State: Connected", textBlocks.First(textBlock => textBlock.Name == "LiveConnectionStateTextBlock").Text);
        Assert.Equal("Session: 909", textBlocks.First(textBlock => textBlock.Name == "LiveSessionIdTextBlock").Text);
        Assert.Single(controls.OfType<Button>(), button => Equals(button.Content, "Save"));
        Assert.NotNull(controls.FirstOrDefault(control => control.Name == "TabControl"));
        Assert.NotNull(controls.OfType<LiveTravelPlotDesktopView>().FirstOrDefault());
        Assert.NotNull(controls.OfType<LiveVelocityPlotDesktopView>().FirstOrDefault());
        Assert.NotNull(controls.OfType<LiveImuPlotDesktopView>().FirstOrDefault());
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