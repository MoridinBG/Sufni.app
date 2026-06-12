using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface IDialogService
{
    public Task<PromptResult> ShowCloseConfirmationAsync(bool isSaveEnabled = true);
    public Task<TileLayerConfig?> ShowAddTileLayerDialogAsync();
    public Task<PromptResult> ShowContentDialogAsync(object contentViewModel, DialogOptions options);
    public Task<bool> ShowConfirmationAsync(string title, string message);
}
