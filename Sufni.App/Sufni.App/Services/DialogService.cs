using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.Models;
using Sufni.App.Views;
using Sufni.App.Views.Controls;

namespace Sufni.App.Services;

public class DialogService : IDialogService
{
    private Window? owner;

    public void SetOwner(Window owner)
    {
        this.owner = owner;
    }

    public async Task<PromptResult> ShowCloseConfirmationAsync(bool isSaveEnabled = true)
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");

        DialogWindow dialog;
        if (isSaveEnabled)
        {
            dialog = new YesNoCancelDialogWindow("Save?", "You have unsaved changes. Save before closing?");
        }
        else
        {
            dialog = new OkCancelDialogWindow("Close?",
                "Page cannot be saved due to missing or wrong data. Are you sure you want to close it?");
        }
        return await dialog.ShowDialogAsync(owner);
    }

    public Task<TileLayerConfig?> ShowAddTileLayerDialogAsync()
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");
        
        var tcs = new TaskCompletionSource<TileLayerConfig?>();
        var window = new Window
        {
            Title = "Add Custom Layer",
            Width = 400,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var content = new AddTileLayerView();
        content.Finished += (s, result) =>
        {
            tcs.TrySetResult(result);
            window.Close();
        };

        window.Content = content;
        
        window.ShowDialog(owner);
        return tcs.Task;
            }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");

        var dialog = new OkCancelDialogWindow(title, message);
        var result = await dialog.ShowDialogAsync(owner);
        return result == PromptResult.Ok;
    }
}