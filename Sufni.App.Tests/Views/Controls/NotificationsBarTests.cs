using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class NotificationsBarTests
{
    [AvaloniaFact]
    public async Task NotificationsBar_HidesItself_WhenThereAreNoNotifications()
    {
        var view = new NotificationsBar
        {
            DataContext = new TestViewModel()
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            Assert.False(view.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task NotificationsBar_ShowsEachNotificationMessage()
    {
        var viewModel = new TestViewModel();
        viewModel.Notifications.Add("Import finished");
        viewModel.Notifications.Add("Sync complete");

        var view = new NotificationsBar
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var listBox = view.FindControl<ListBox>("NotificationsListBox");
            Assert.NotNull(listBox);

            var items = Assert.IsAssignableFrom<IEnumerable>(listBox!.ItemsSource).Cast<object>().ToArray();

            Assert.True(view.IsVisible);
            Assert.Equal(2, items.Length);
            Assert.Contains("Import finished", items);
            Assert.Contains("Sync complete", items);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task NotificationsBar_CloseButton_ClearsNotifications()
    {
        var viewModel = new TestViewModel();
        viewModel.Notifications.Add("Pairing succeeded");

        var view = new NotificationsBar
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var closeButton = view.FindControl<Button>("CloseButton");
            Assert.NotNull(closeButton);
            Assert.NotNull(closeButton!.Command);

            closeButton.Command.Execute(closeButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Empty(viewModel.Notifications);
            Assert.False(view.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}