using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

using Sufni.App.Infrastructure;
namespace Sufni.App.Shared.Views.Dialogs;

// Desktop (window-mode) generic multi-choice prompt. Renders one button per
// DialogChoice and returns the id of the chosen one, or null if the window is
// closed via its chrome without a choice.
public partial class ChoiceDialogWindow : Window
{
    private readonly TaskCompletionSource<string?> tcs = new();

    public ChoiceDialogWindow() : this("Dialog", string.Empty, new List<DialogChoice>()) { }

    public ChoiceDialogWindow(string title, string message, IReadOnlyList<DialogChoice> choices)
    {
        Title = title;
        InitializeComponent();
        MessageText.Text = message;

        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.Label };
            if (choice.IsDefault)
            {
                button.Classes.Add("accent");
                button.IsDefault = true;
            }

            // Capture the id per iteration so every button reports its own choice.
            var id = choice.Id;
            button.Click += (_, _) =>
            {
                tcs.TrySetResult(id);
                Close();
            };
            ButtonPanel.Children.Add(button);
        }

        // Closing via the window chrome is equivalent to dismissing the prompt.
        Closed += (_, _) => tcs.TrySetResult(null);
    }

    public Task<string?> ShowDialogAsync(Window owner)
    {
        ShowDialog(owner);
        return tcs.Task;
    }
}
