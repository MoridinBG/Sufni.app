using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Views;

public class MainWindowTests
{
    [AvaloniaFact]
    public async Task MainWindow_TabDragFeedback_FadesDraggedTabAndShowsInsertionIndicator()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        await using var mounted = await MountAsync(new MainWindow
        {
            Width = 900,
            Height = 700,
        });

        var tabItem = new TabStripItem();

        mounted.Window.BeginTabDragFeedback(tabItem);
        mounted.Window.ShowTabDropIndicator(120);

        Assert.True(mounted.Window.IsTabDragFeedbackVisible);
        Assert.True(tabItem.Opacity < 1);
        Assert.True(mounted.Window.IsTabDropIndicatorVisible);
        Assert.True(mounted.Window.TabDropIndicatorX > 0);

        mounted.Window.EndTabDragFeedback();

        Assert.False(mounted.Window.IsTabDragFeedbackVisible);
        Assert.Equal(1, tabItem.Opacity);
        Assert.False(mounted.Window.IsTabDropIndicatorVisible);
    }

    private static async Task<MountedMainWindow> MountAsync(MainWindow window)
    {
        window.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        await ViewTestHelpers.FlushDispatcherAsync();

        return new MountedMainWindow(window);
    }
}

internal sealed class MountedMainWindow(MainWindow window) : IAsyncDisposable
{
    public MainWindow Window { get; } = window;

    public async ValueTask DisposeAsync()
    {
        Window.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
