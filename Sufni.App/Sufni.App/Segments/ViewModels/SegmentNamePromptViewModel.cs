using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Infrastructure;
namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// Content of the dialog that asks for a saved segment's name. The dialog
/// completes on confirm and on cancel; <see cref="ConfirmedName"/> stays null
/// after cancel.
/// </summary>
public sealed partial class SegmentNamePromptViewModel : ObservableObject, IContentDialogCompletionSource
{
    private readonly Func<string, string?> validate;

    [ObservableProperty] public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    public partial string? ValidationMessage { get; private set; }

    public SegmentNamePromptViewModel(string title, string confirmText, string name, Func<string, string?> validate)
    {
        Title = title;
        ConfirmText = confirmText;
        Text = name;
        this.validate = validate;
    }

    public string Title { get; }
    public string ConfirmText { get; }
    public bool HasValidationMessage => ValidationMessage is not null;
    public string? ConfirmedName { get; private set; }

    public event EventHandler? Completed;

    partial void OnTextChanged(string value) => ValidationMessage = null;

    [RelayCommand]
    private void Cancel() => Completed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Confirm()
    {
        var name = Text.Trim();
        ValidationMessage = validate(name);
        if (ValidationMessage is not null)
        {
            return;
        }

        ConfirmedName = name;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
