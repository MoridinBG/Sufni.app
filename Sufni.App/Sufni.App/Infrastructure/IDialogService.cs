using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.Infrastructure;

public interface IDialogService
{
    public Task<PromptResult> ShowCloseConfirmationAsync(bool isSaveEnabled = true);
    public Task<TileLayerConfig?> ShowAddTileLayerDialogAsync();
    public Task<PromptResult> ShowContentDialogAsync(object contentViewModel, DialogOptions options);
    public Task<bool> ShowConfirmationAsync(string title, string message);

    /// <summary>
    /// Shows a modal prompt offering <paramref name="choices"/> as buttons and
    /// returns the id of the chosen one, or null if the prompt was dismissed
    /// without a choice.
    /// </summary>
    public Task<string?> ShowChoiceAsync(string title, string message, IReadOnlyList<DialogChoice> choices);

    /// <summary>
    /// Runs <paramref name="work"/> behind a modal determinate progress dialog,
    /// closing the dialog when the work completes (or faults) and returning its
    /// result. The work reports progress through the supplied
    /// <see cref="IProgress{T}"/>.
    /// </summary>
    public Task<T> ShowProgressAsync<T>(string title, Func<IProgress<DialogProgress>, Task<T>> work);
}
