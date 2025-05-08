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

    public async Task<PromptResult> ShowSaveConfirmationAsync()
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");
        
        var dialog = new YesNoCancelDialogWindow("Save?", "You have unsaved changes. Save before closing?");
        return await dialog.ShowDialogAsync(owner);
    }
}