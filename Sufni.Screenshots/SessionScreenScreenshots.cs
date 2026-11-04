using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Sufni.App.Sessions.Detail.Views.Editors;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Tests.Sessions.Detail.Views.Editors;

namespace Sufni.Screenshots;

public class SessionScreenScreenshots
{
    [AvaloniaTheory]
    [InlineData(false, "loading")]
    [InlineData(true, "loading")]
    [InlineData(false, "error")]
    [InlineData(true, "error")]
    [InlineData(false, "incomplete")]
    [InlineData(true, "incomplete")]
    public async Task ScreenOverlay(bool light, string state)
    {
        var host = new FakeShellHostViewModel
        {
            ScreenState = state switch
            {
                "loading" => SessionScreenPresentationState.Loading("Reading session telemetry...", 0.4),
                "error" => SessionScreenPresentationState.Error("The session could not be read. Try opening it again."),
                _ => SessionScreenPresentationState.IncompleteLocalData("Local session data is incomplete. Run sync and try again."),
            },
        };
        var view = new SessionShellMobileView { DataContext = host };
        var window = new Window
        {
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
            Width = 402, Height = 778, Content = view,
        };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var directory = Path.Combine(AppContext.BaseDirectory, "screenshots", "session-screen");
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{(light ? "light" : "dark")}-{state}.png"));
        }
        finally
        {
            window.Close();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
