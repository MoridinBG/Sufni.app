using Sufni.App.Shared.Models;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;

using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Shared.Views.Controls;

[Collection("Ui")]
public class ErrorMessagesBarTests
{
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("Technical diagnostic")]
    public async Task ErrorMessagesBar_KeepsDetailsCollapsedUntilExpanded(string? details)
    {
        ViewTestHelpers.EnsureFluentTheme();
        var viewModel = new TestViewModel();
        var failure = FailurePresentation.From(VideoImportFailureReason.Import, details);
        viewModel.ErrorMessages.Add(failure);
        var view = new ErrorMessagesBar { DataContext = viewModel };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            var summary = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Name == "FailureSummary");
            var expander = view.GetVisualDescendants().OfType<Expander>().Single();
            Assert.Equal(failure.Summary, summary.Text);
            Assert.Equal(details is not null, expander.IsVisible);
            Assert.False(expander.IsExpanded);
            if (details is null) return;

            expander.IsExpanded = true;
            await ViewTestHelpers.FlushDispatcherAsync();

            var detailText = expander.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
            Assert.Equal(details, detailText.Text);
            Assert.True(detailText.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

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
        viewModel.ErrorMessages.Add(new FailurePresentation("Fork sensor is missing"));
        viewModel.ErrorMessages.Add(new FailurePresentation("Shock sensor is missing"));

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
            Assert.Contains(viewModel.ErrorMessages[0], items);
            Assert.Contains(viewModel.ErrorMessages[1], items);
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
        viewModel.ErrorMessages.Add(new FailurePresentation("Board id is required"));

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