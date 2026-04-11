using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class ErrorMessagesBarTests
{
    [AvaloniaFact]
    public async Task ErrorMessagesBar_HidesItself_WhenThereAreNoErrors()
    {
        var view = new ErrorMessagesBar
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
    public async Task ErrorMessagesBar_ShowsEachErrorMessage()
    {
        var viewModel = new TestViewModel();
        viewModel.ErrorMessages.Add("Fork sensor is missing");
        viewModel.ErrorMessages.Add("Shock sensor is missing");

        var view = new ErrorMessagesBar
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var errorListBox = view.FindControl<ListBox>("ErrorListBox");
            Assert.NotNull(errorListBox);
            var items = Assert.IsAssignableFrom<IEnumerable>(errorListBox!.ItemsSource).Cast<object>().ToArray();

            Assert.True(view.IsVisible);
            Assert.Equal(2, items.Length);
            Assert.Contains("Fork sensor is missing", items);
            Assert.Contains("Shock sensor is missing", items);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task ErrorMessagesBar_CloseButton_ClearsAllErrors()
    {
        var viewModel = new TestViewModel();
        viewModel.ErrorMessages.Add("Board id is required");

        var view = new ErrorMessagesBar
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

            Assert.Empty(viewModel.ErrorMessages);
            Assert.False(view.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}