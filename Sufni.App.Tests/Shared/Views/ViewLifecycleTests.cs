using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Shared.Views;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shared.Views;

[Collection("Ui")]
public class ViewLifecycleTests
{
    [AvaloniaFact]
    public async Task LoadedAndUnloadedCommands_RunOnEveryLoadAndUnload()
    {
        var loadedCount = 0;
        var unloadedCount = 0;
        var view = new Border();
        ViewLifecycle.SetLoadedCommand(view, new RelayCommand(() => loadedCount++));
        ViewLifecycle.SetUnloadedCommand(view, new RelayCommand(() => unloadedCount++));

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            Assert.Equal((1, 0), (loadedCount, unloadedCount));

            await SetHostContentAsync(host, null);
            Assert.Equal((1, 1), (loadedCount, unloadedCount));

            await SetHostContentAsync(host, view);
            await SetHostContentAsync(host, null);
            Assert.Equal((2, 2), (loadedCount, unloadedCount));
        }
        finally
        {
            await CloseAsync(host);
        }
    }

    [AvaloniaFact]
    public async Task VisualTreeCommands_RunOnEveryAttachAndDetach()
    {
        var attachedCount = 0;
        var detachedCount = 0;
        var view = new Border();
        ViewLifecycle.SetAttachedToVisualTreeCommand(view, new RelayCommand(() => attachedCount++));
        ViewLifecycle.SetDetachedFromVisualTreeCommand(view, new RelayCommand(() => detachedCount++));

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            Assert.Equal((1, 0), (attachedCount, detachedCount));

            await SetHostContentAsync(host, null);
            Assert.Equal((1, 1), (attachedCount, detachedCount));

            await SetHostContentAsync(host, view);
            await SetHostContentAsync(host, null);
            Assert.Equal((2, 2), (attachedCount, detachedCount));
        }
        finally
        {
            await CloseAsync(host);
        }
    }

    [AvaloniaFact]
    public async Task LoadedCommand_ReceivesLoadedCommandParameter()
    {
        var parameter = new object();
        object? received = null;
        var view = new Border();
        ViewLifecycle.SetLoadedCommand(view, new RelayCommand<object?>(value => received = value));
        ViewLifecycle.SetLoadedCommandParameter(view, parameter);

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            Assert.Same(parameter, received);
        }
        finally
        {
            await CloseAsync(host);
        }
    }

    [AvaloniaFact]
    public async Task LoadedCommand_DoesNotRun_WhenCanExecuteIsFalse()
    {
        var loadedCount = 0;
        var view = new Border();
        ViewLifecycle.SetLoadedCommand(view, new RelayCommand(() => loadedCount++, () => false));

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            Assert.Equal(0, loadedCount);
        }
        finally
        {
            await CloseAsync(host);
        }
    }

    [AvaloniaFact]
    public async Task ReplacedLoadedCommand_RunsOnlyTheNewCommandOnce()
    {
        var firstCount = 0;
        var secondCount = 0;
        var view = new Border();
        ViewLifecycle.SetLoadedCommand(view, new RelayCommand(() => firstCount++));
        ViewLifecycle.SetLoadedCommand(view, new RelayCommand(() => secondCount++));

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            Assert.Equal((0, 1), (firstCount, secondCount));
        }
        finally
        {
            await CloseAsync(host);
        }
    }

    private static async Task SetHostContentAsync(Window host, Control? content)
    {
        host.Content = content;
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static async Task CloseAsync(Window host)
    {
        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
