using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.ViewModels;

public sealed record TerrainLabelClassOption(TerrainLabelClass Class, string Name);

/// <summary>
/// Content of the dialog that asks for the class of a terrain label of one kind. The dialog completes on confirm and on
/// cancel; <see cref="ConfirmedClass"/> stays null after cancel. A validation message blocks the confirm.
/// </summary>
public sealed partial class TerrainLabelClassPromptViewModel : ObservableObject, IContentDialogCompletionSource
{
    public TerrainLabelClassPromptViewModel(string title, string confirmText, TerrainLabelKind kind, TerrainLabelClass? selected,
        string? validationMessage)
    {
        Title = title;
        ConfirmText = confirmText;
        Classes = kind == TerrainLabelKind.Steepness
            ? [.. Enum.GetValues<GradeBand>().Select(band => Option(new TerrainLabelClass.Steepness(band)))]
            : [.. Enum.GetValues<RoughnessClass>().Select(level => Option(new TerrainLabelClass.Roughness(level)))];
        SelectedClass = selected;
        ValidationMessage = validationMessage;
    }

    /// <summary>Shows the prompt in a dialog and returns the confirmed class, or null after cancel.</summary>
    public static async Task<TerrainLabelClass?> AskAsync(IDialogService dialogService, string title, string confirmText,
        TerrainLabelKind kind, TerrainLabelClass? selected, string? validationMessage)
    {
        var prompt = new TerrainLabelClassPromptViewModel(title, confirmText, kind, selected, validationMessage);
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
    public TerrainLabelClass? ConfirmedClass { get; private set; }

    public IReadOnlyList<TerrainLabelClassOption> Classes { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial TerrainLabelClass? SelectedClass { get; set; }

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

    private static TerrainLabelClassOption Option(TerrainLabelClass labelClass) => new(labelClass, TerrainClassText.Name(labelClass));
}
