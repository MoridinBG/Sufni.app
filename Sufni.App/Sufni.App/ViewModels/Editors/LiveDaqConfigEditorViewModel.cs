using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services.Management;

namespace Sufni.App.ViewModels.Editors;

public sealed partial class LiveDaqConfigEditorViewModel : ViewModelBase
{
    private readonly DaqConfigDocument document;
    private readonly Func<byte[], CancellationToken, Task<DaqManagementResult>> uploadAsync;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isSaving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    private string? saveErrorMessage;

    public LiveDaqConfigEditorViewModel(
        DaqConfigDocument document,
        Func<byte[], CancellationToken, Task<DaqManagementResult>> uploadAsync)
    {
        this.document = document;
        this.uploadAsync = uploadAsync;

        foreach (var fieldValue in document.GetFieldValues())
        {
            Fields.Add(new LiveDaqConfigFieldRowViewModel(fieldValue, OnFieldValueChanged));
        }

        ValidateCurrentValues();
    }

    public ObservableCollection<LiveDaqConfigFieldRowViewModel> Fields { get; } = [];

    public bool HasSaveError => !string.IsNullOrWhiteSpace(SaveErrorMessage);

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
        SaveErrorMessage = null;
        if (!ValidateCurrentValues())
        {
            return;
        }

        IsSaving = true;
        try
        {
            var result = await uploadAsync(document.BuildBytes(GetEditableValues()), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            switch (result)
            {
                case DaqManagementResult.Ok:
                    Complete();
                    break;
                case DaqManagementResult.Error error:
                    SaveErrorMessage = error.Message;
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SaveErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => !IsSaving && !IsCompleted;

    private void OnFieldValueChanged()
    {
        SaveErrorMessage = null;
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

    private IReadOnlyDictionary<string, string> GetEditableValues() =>
        Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);

    private void Complete()
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}