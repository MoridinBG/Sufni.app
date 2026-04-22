using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class OkCancelDialogWindowTests
{
    [AvaloniaFact]
    public async Task OkCancelDialogWindow_OkButton_CompletesDialogWithOk()
    {
        var owner = new Window();
        owner.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var dialog = new OkCancelDialogWindow("Close?", "Discard changes?");

        try
        {
            var resultTask = dialog.ShowDialogAsync(owner);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal("Discard changes?", dialog.FindControl<TextBlock>("MessageText")!.Text);

            var okButton = dialog.FindControl<Button>("OkButton");
            Assert.NotNull(okButton);
            okButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var result = await resultTask;
            Assert.Equal(PromptResult.Ok, result);
        }
        finally
        {
            if (dialog.IsVisible)
            {
                dialog.Close();
            }

            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task OkCancelDialogWindow_CancelButton_CompletesDialogWithCancel()
    {
        var owner = new Window();
        owner.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var dialog = new OkCancelDialogWindow("Close?", "Discard changes?");

        try
        {
            var resultTask = dialog.ShowDialogAsync(owner);
            await ViewTestHelpers.FlushDispatcherAsync();

            var cancelButton = dialog.FindControl<Button>("CancelButton");
            Assert.NotNull(cancelButton);
            cancelButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var result = await resultTask;
            Assert.Equal(PromptResult.Cancel, result);
        }
        finally
        {
            if (dialog.IsVisible)
            {
                dialog.Close();
            }

            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}