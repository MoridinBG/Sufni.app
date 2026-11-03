using System.Collections.Immutable;
using Sufni.App.Shared.Models;
using Sufni.App.LiveDaq.Coordinators;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed partial class LiveDaqConfigEditorViewModel : ObservableObject, IContentDialogCompletionSource, IDisposable
{
    private readonly Func<ImmutableDictionary<string, string>, CancellationToken, Task<LiveDaqManagementResult>> uploadAsync;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsCompleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial FailurePresentation? SaveError { get; set; }

    public LiveDaqConfigEditorViewModel(
        DaqConfigDocument document,
        Func<ImmutableDictionary<string, string>, CancellationToken, Task<LiveDaqManagementResult>> uploadAsync)
    {
        this.uploadAsync = uploadAsync;

        foreach (var fieldValue in document.GetFieldValues())
        {
            Fields.Add(new LiveDaqConfigFieldRowViewModel(fieldValue, OnFieldValueChanged));
        }

        ValidateCurrentValues();
    }

    public ObservableCollection<LiveDaqConfigFieldRowViewModel> Fields { get; } = [];

    public bool HasSaveError => SaveError is not null;

    public bool HasValidationErrors => Fields.Any(row => row.HasValidationMessage);

    public event EventHandler? Completed;

    [RelayCommand]
    private void Cancel()
    {
        Complete();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save(CancellationToken cancellationToken)
    {
        SaveError = null;
        if (!ValidateCurrentValues())
        {
            return;
        }

        IsSaving = true;
        try
        {
            var result = await uploadAsync(GetEditableValues(), cancellationToken);
            if (cancellationToken.IsCancellationRequested || IsCompleted)
            {
                return;
            }

            if (result is LiveDaqManagementResult.ConfigWritten) Complete();
            else SaveError = LiveDaqMessages.ManagementFailure(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => !IsSaving && !IsCompleted;

    private void OnFieldValueChanged()
    {
        SaveError = null;
        ValidateCurrentValues();
    }

    private bool ValidateCurrentValues()
    {
        var errors = DaqConfigValidator.Validate(GetEditableValues());
        foreach (var field in Fields)
        {
            field.ValidationMessage = errors.TryGetValue(field.Key, out var error) ? error : null;
        }

        OnPropertyChanged(nameof(HasValidationErrors));
        return errors.Count == 0;
    }

    private ImmutableDictionary<string, string> GetEditableValues() =>
        Fields.ToImmutableDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);

    public void Dispose() => SaveCommand.Cancel();

    private void Complete()
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;
        SaveCommand.Cancel();
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
