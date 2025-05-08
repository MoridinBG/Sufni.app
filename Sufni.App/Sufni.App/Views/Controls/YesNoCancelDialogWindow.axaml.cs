using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sufni.App.Views.Controls;

public enum PromptResult { Yes, No, Cancel }

public partial class YesNoCancelDialogWindow : Window
{
    private TaskCompletionSource<PromptResult> tcs = null!;

    public YesNoCancelDialogWindow() : this("Confirm", "Yes? No? Cancel?") { }
    public YesNoCancelDialogWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        
        Closed += (_, _) => tcs.TrySetResult(PromptResult.Cancel);
    }

    public Task<PromptResult> ShowDialogAsync(Window owner)
    {
        tcs = new TaskCompletionSource<PromptResult>();
        ShowDialog(owner);
        return tcs.Task;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e)
    {
        tcs.TrySetResult(PromptResult.Yes);
        Close();
    }

    private void No_Click(object? sender, RoutedEventArgs e)
    {
        tcs.TrySetResult(PromptResult.No);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        tcs.TrySetResult(PromptResult.Cancel);
        Close();
    }
}