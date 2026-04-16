using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.BikeEditing;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.Editors.Bike;

public sealed partial class LeverageRatioEditorViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<LeverageRatioImportResult>>? importCsvAsync;

    [ObservableProperty] private LeverageRatio? value;
    [ObservableProperty] private ObservableCollection<LeverageRatioPoint> pointsView = [];
    [ObservableProperty] private string[] validationErrors = [];
    [ObservableProperty] private CoordinateList? leverageRatioPlotData;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool isBusy;

    public event EventHandler? Changed;

    public int PointCount => Value?.Points.Count ?? 0;
    public double? MaxShockStroke => Value?.MaxShockStroke;
    public double? MaxWheelTravel => Value?.MaxWheelTravel;

    public LeverageRatioEditorViewModel(
        Func<CancellationToken, Task<LeverageRatioImportResult>>? importCsvAsync = null,
        bool canEdit = true)
    {
        this.importCsvAsync = importCsvAsync;
        CanEdit = canEdit;
    }

    partial void OnValueChanged(LeverageRatio? value)
    {
        PointsView = value is null
            ? []
            : [.. value.Points];
        LeverageRatioPlotData = value is null
            ? null
            : BuildCoordinateList(value);

        OnPropertyChanged(nameof(PointCount));
        OnPropertyChanged(nameof(MaxShockStroke));
        OnPropertyChanged(nameof(MaxWheelTravel));
        ImportCsvCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanEditChanged(bool value)
    {
        ImportCsvCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    partial void OnValidationErrorsChanged(string[] value)
    {
        ClearCommand.NotifyCanExecuteChanged();
    }

    public LeverageRatio? BuildCurrent() => Value;

    public void ReplaceState(LeverageRatio? initial)
    {
        ValidationErrors = [];
        Value = initial;
    }

    private bool CanImportCsv() => CanEdit && importCsvAsync is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanImportCsv))]
    private async Task ImportCsv()
    {
        if (importCsvAsync is null)
        {
            return;
        }

        IsBusy = true;
        ImportCsvCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await importCsvAsync(CancellationToken.None);
            switch (result)
            {
                case LeverageRatioImportResult.Imported imported:
                    ValidationErrors = [];
                    Value = imported.Value;
                    Changed?.Invoke(this, EventArgs.Empty);
                    break;

                case LeverageRatioImportResult.Invalid invalid:
                    ValidationErrors = invalid.ErrorMessages;
                    break;

                case LeverageRatioImportResult.Failed failed:
                    ValidationErrors = [failed.ErrorMessage];
                    break;

                case LeverageRatioImportResult.Canceled:
                    break;
            }
        }
        finally
        {
            IsBusy = false;
            ImportCsvCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanClear() => CanEdit && (Value is not null || ValidationErrors.Length > 0);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        if (Value is null && ValidationErrors.Length == 0)
        {
            return;
        }

        ValidationErrors = [];
        Value = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static CoordinateList BuildCoordinateList(LeverageRatio leverageRatio)
    {
        var samples = leverageRatio.DeriveLeverageRatioSamples();
        return new CoordinateList(
            [.. samples.Select(sample => sample.WheelTravelMm)],
            [.. samples.Select(sample => sample.Ratio)]);
    }
}