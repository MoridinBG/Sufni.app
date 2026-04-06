using System.Threading.Tasks;
using Avalonia.Controls;
using Sufni.App.Views.Controls;

namespace Sufni.App.Services;

public interface IDialogService
{
    public void SetOwner(Window owner);
    public Task<PromptResult> ShowCloseConfirmationAsync(bool isSaveEnabled = true);
    public Task<bool> ShowConfirmationAsync(string title, string message);
}