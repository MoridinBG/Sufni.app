using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.ViewModels;

public sealed record TerrainLabelClassOption(RoughnessClass Class, string Name);

/// <summary>
/// Content of the dialog that asks for the class of a terrain label. The dialog completes on confirm and on cancel;
/// <see cref="ConfirmedClass"/> stays null after cancel. A validation message blocks the confirm.
/// </summary>
public sealed partial class TerrainLabelClassPromptViewModel : ObservableObject, IContentDialogCompletionSource
{
    public TerrainLabelClassPromptViewModel(string title, string confirmText, RoughnessClass? selected, string? validationMessage)
    {
        Title = title;
        ConfirmText = confirmText;
        SelectedClass = selected;
        ValidationMessage = validationMessage;
    }

    /// <summary>Shows the prompt in a dialog and returns the confirmed class, or null after cancel.</summary>
    public static async Task<RoughnessClass?> AskAsync(IDialogService dialogService, string title, string confirmText,
        RoughnessClass? selected, string? validationMessage)
    {
        var prompt = new TerrainLabelClassPromptViewModel(title, confirmText, selected, validationMessage);
        await dialogService.ShowContentDialogAsync(
            prompt,
            new DialogOptions(
                Title: title,
                Width: 360,
                Height: 420,
                MinWidth: 320,
                MinHeight: 360,
                CanResize: false));
        return prompt.ConfirmedClass;
    }

    public string Title { get; }
    public string ConfirmText { get; }
    public string? ValidationMessage { get; }
    public bool HasValidationMessage => ValidationMessage is not null;
    public RoughnessClass? ConfirmedClass { get; private set; }

    public IReadOnlyList<TerrainLabelClassOption> Classes { get; } =
        [.. Enum.GetValues<RoughnessClass>().Select(level => new TerrainLabelClassOption(level, TerrainClassText.Name(level)))];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial RoughnessClass? SelectedClass { get; set; }

    public event EventHandler? Completed;

    [RelayCommand]
    private void Cancel() => Completed?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        ConfirmedClass = SelectedClass;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanConfirm() => SelectedClass is not null && ValidationMessage is null;
}
