using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.Tests.TestSupport;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.Screenshots;

public class StartupScreenshots
{
    [AvaloniaTheory]
    [InlineData(1000, 760, false, false)]
    [InlineData(1000, 760, false, true)]
    [InlineData(1000, 760, true, false)]
    [InlineData(1000, 760, true, true)]
    [InlineData(402, 778, true, false)]
    [InlineData(402, 778, true, true)]
    public async Task DatabaseFailure(int width, int height, bool expanded, bool light)
    {
        var startup = Substitute.For<IAppStartup>();
        startup.Observe().Returns(Observable.Return<ShellStartupState>(
            new ShellStartupState.Failed(StartupFailureReason.Database,
                "This operation cannot be performed on a default instance of ImmutableArray<T>. Consider initializing the array, or checking the ImmutableArray<T>.IsDefault property.")));
        startup.LogPath.Returns("/Users/example/Library/Application Support/Sufni.App/logs/LOG-20261103-091726.log");
        var dispatcher = new InlineUiThreadDispatcher();
        var clock = new FakeTimeProvider();
        using var feedback = TestShellFeedback.Create();
        using var root = new ShellRootViewModel(startup, Substitute.For<IAppShutdown>(), feedback,
            new ShellWorkspaceViewModel(dispatcher, clock), TestAppEnvironment.Create(UiLayoutProfile.Workspace),
            Substitute.For<IPlotZoomState>(), dispatcher, clock);
        var view = new StartupView { DataContext = root };
        var window = new Window { RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Width = width, Height = height, Content = view };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (expanded)
            {
                view.FindControl<Expander>("StartupDetails")!.IsExpanded = true;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var directory = Path.Combine(AppContext.BaseDirectory, "screenshots", "startup");
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"{(light ? "light" : "dark")}-failure-{width}-{(expanded ? "expanded" : "collapsed")}.png"));
        }
        finally
        {
            window.Close();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
