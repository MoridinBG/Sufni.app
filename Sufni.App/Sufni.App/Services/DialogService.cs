using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
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
}