using System.Threading.Tasks;
using Avalonia.Controls;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface IDialogService
{
    public void SetOwner(Window owner);
    public void SetOverlayHost(Control host);
    public Task<PromptResult> ShowCloseConfirmationAsync(bool isSaveEnabled = true);
    public Task<TileLayerConfig?> ShowAddTileLayerDialogAsync();
    public Task<PromptResult> ShowContentDialogAsync(object contentViewModel, DialogOptions options);
    public Task<bool> ShowConfirmationAsync(string title, string message);
}
