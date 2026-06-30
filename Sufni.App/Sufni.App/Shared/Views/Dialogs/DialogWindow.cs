using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Sufni.App.Infrastructure;
namespace Sufni.App.Shared.Views.Dialogs;

public partial class DialogWindow : Window
{
    private TaskCompletionSource<PromptResult> tcs = null!;

    public DialogWindow(string title)
    {
        Title = title;
        Closed += (_, _) => tcs.TrySetResult(PromptResult.Cancel);
    }
    
    public Task<PromptResult> ShowDialogAsync(Window owner)
    {
        tcs = new TaskCompletionSource<PromptResult>();
        ShowDialog(owner);
        return tcs.Task;
    }

    protected void Yes_Click(object? sender, RoutedEventArgs e)
    {
        tcs.TrySetResult(PromptResult.Yes);
        Close();
    }

    protected void No_Click(object? sender, RoutedEventArgs e)
    {
        tcs.TrySetResult(PromptResult.No);
        Close();
    }
    
    protected void Ok_Click(object? sender, RoutedEventArgs e)
    {
        tcs.TrySetResult(PromptResult.Ok);
        Close();
    }

    protected void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        tcs.TrySetResult(PromptResult.Cancel);
        Close();
    }
}
