using System.Threading.Tasks;
using Avalonia.Controls;
using Sufni.App.Models;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Controls;

namespace Sufni.App.Services;

public interface IDialogService
{
    public void SetOwner(Window owner);
    public void SetOverlayHost(Control host);
    public Task<PromptResult> ShowCloseConfirmationAsync(bool isSaveEnabled = true);
    public Task<TileLayerConfig?> ShowAddTileLayerDialogAsync();
    public Task ShowLiveDaqConfigEditorDialogAsync(LiveDaqConfigEditorViewModel editor);
    public Task<bool> ShowConfirmationAsync(string title, string message);
}