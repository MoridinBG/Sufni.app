using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models.Telemetry;
using Sufni.App.Services;

namespace Sufni.App.ViewModels.Items;

public partial class CalibrationInputViewModel : ViewModelBase
{
    #region Observable properties

    [ObservableProperty] private string name;
    [ObservableProperty] private double value;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private double originalValue;

    #endregion

    #region Constructors

    public CalibrationInputViewModel(string name)
    {
        OriginalValue = value;
        Name = name;
        Value = 0.0;
        IsDirty = true;
    }

    public CalibrationInputViewModel(string name, double value)
    {
        OriginalValue = value;
        Name = name;
        Value = value;
    }

    #endregion

    #region Property change handlers

    partial void OnValueChanged(double value)
    {
        IsDirty = Math.Abs(value - OriginalValue) > 0.00001;
    }

    #endregion
}

public partial class CalibrationViewModel : ItemViewModelBase
{
    private Calibration calibration;
    public bool IsInDatabase;

    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private CalibrationMethod? selectedCalibrationMethod;

    public ObservableCollection<CalibrationMethod> CalibrationMethods { get; }
    public ObservableCollection<CalibrationInputViewModel> Inputs { get; } = [];

    #endregion

    #region Property change handlers

    partial void OnSelectedCalibrationMethodChanged(CalibrationMethod? value)
    {
        if (value == null)
        {
            IsDirty = true;
            return;
        }

        Inputs.Clear();
        if (value.Id == calibration.MethodId)
        {
            foreach (var input in calibration.Inputs)
            {
                var ivm = new CalibrationInputViewModel(input.Key, input.Value);
                ivm.PropertyChanged += (_, _) =>
                {
                    SaveCommand.NotifyCanExecuteChanged();
                    ResetCommand.NotifyCanExecuteChanged();
                };
                Inputs.Add(ivm);
            }
        }
        else
        {
            foreach (var input in value.Properties.Inputs)
            {
                var ivm = new CalibrationInputViewModel(input);
                ivm.PropertyChanged += (_, _) =>
                {
                    SaveCommand.NotifyCanExecuteChanged();
                    ResetCommand.NotifyCanExecuteChanged();
                };
                Inputs.Add(ivm);
            }
        }
    }

    #endregion

    #region Constructors

    public CalibrationViewModel()
    {
        CalibrationMethods = [];
        calibration = new Calibration();
        ResetImplementation();
    }

    public CalibrationViewModel(Calibration calibration, ObservableCollection<CalibrationMethod> calibrationMethods, bool fromDatabase)
    {
        this.calibration = calibration;
        IsInDatabase = fromDatabase;
        Id = calibration.Id;
        CalibrationMethods = calibrationMethods;
        ResetImplementation();
    }

    #endregion

    #region ItemViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != calibration.Name ||
            SelectedCalibrationMethod == null || SelectedCalibrationMethod.Id != calibration.MethodId ||
            Inputs.Any(i => i.IsDirty);
    }

    protected override async Task SaveImplementation()
    {
        Debug.Assert(SelectedCalibrationMethod != null, nameof(SelectedCalibrationMethod) + " != null");

        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var newCalibration = new Calibration(
                Id,
                Name ?? $"calibration #{Id}",
                SelectedCalibrationMethod.Id,
                Inputs.ToDictionary(input => input.Name, input => input.Value));
            Id = await databaseService.PutCalibrationAsync(newCalibration);

            calibration = newCalibration;
            foreach (var input in Inputs)
            {
                input.OriginalValue = input.Value;
                input.IsDirty = false;
            }

            if (!IsInDatabase)
            {
                var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
                Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");
                mainPagesViewModel.CalibrationsPage.OnAdded(this);
            }

            IsDirty = false;
            IsInDatabase = true;

            OpenPreviousPage();
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Calibration could not be saved: {e.Message}");
        }
    }

    protected override Task ResetImplementation()
    {
        try
        {
            Name = calibration.Name;
            SelectedCalibrationMethod = null; // so that OnSelectedCalibrationMethodChanged kicks in, resetting inputs too
            SelectedCalibrationMethod = CalibrationMethods.First(m => m.Id == calibration.MethodId);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Calibration could not be reset: {e.Message}");
        }

        return Task.CompletedTask;
    }

    protected override bool CanDelete()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        return !mainPagesViewModel.SetupsPage.Items.Any(s =>
            s is SetupViewModel svm &&
            ((svm.SelectedFrontCalibration != null && svm.SelectedFrontCalibration.Id == Id) ||
            (svm.SelectedRearCalibration != null && svm.SelectedRearCalibration.Id == Id)));
    }

    #endregion
}