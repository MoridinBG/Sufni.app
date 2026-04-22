using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class YesNoCancelDialogWindowTests
{
    [AvaloniaFact]
    public async Task YesNoCancelDialogWindow_YesButton_CompletesDialogWithYes()
    {
        var result = await ShowAndPressAsync("YesButton");
        Assert.Equal(PromptResult.Yes, result);
    }

    [AvaloniaFact]
    public async Task YesNoCancelDialogWindow_NoButton_CompletesDialogWithNo()
    {
        var result = await ShowAndPressAsync("NoButton");
        Assert.Equal(PromptResult.No, result);
    }

    [AvaloniaFact]
    public async Task YesNoCancelDialogWindow_CancelButton_CompletesDialogWithCancel()
    {
        var result = await ShowAndPressAsync("CancelButton");
        Assert.Equal(PromptResult.Cancel, result);
    }

    private static async Task<PromptResult> ShowAndPressAsync(string buttonName)
    {
        var owner = new Window();
        owner.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var dialog = new YesNoCancelDialogWindow("Save?", "Save before closing?");

        try
        {
            var resultTask = dialog.ShowDialogAsync(owner);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal("Save before closing?", dialog.FindControl<TextBlock>("MessageText")!.Text);

            var button = dialog.FindControl<Button>(buttonName);
            Assert.NotNull(button);
            button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            return await resultTask;
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