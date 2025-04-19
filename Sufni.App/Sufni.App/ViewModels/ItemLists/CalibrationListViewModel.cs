using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using Sufni.App.Models.Telemetry;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public partial class CalibrationListViewModel : ItemListViewModelBase
{
    [ObservableProperty] private bool hasCalibrationMethods;
    [ObservableProperty] private bool hasCalibrations;

    private ObservableCollection<CalibrationMethod> CalibrationMethods { get; } = [];

    public CalibrationListViewModel()
    {
        Source.CountChanged.Subscribe(_ => { HasCalibrations = Source.Count != 0; });
        CalibrationMethods.CollectionChanged += (_, _) => { HasCalibrationMethods = CalibrationMethods.Count != 0; };
    }

    private async Task LoadCalibrationMethodsAsync()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var methods = await databaseService.GetCalibrationMethodsAsync();

            foreach (var method in methods)
            {
                CalibrationMethods.Add(method);
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Calibration methods: {e.Message}");
        }
    }

    private async Task LoadCalibrationsAsync()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var calibrationsList = await databaseService.GetCalibrationsAsync();
            foreach (var calibration in calibrationsList)
            {
                Source.AddOrUpdate(new CalibrationViewModel(calibration, CalibrationMethods, true));
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Calibrations: {e.Message}");
        }
    }

    public override async Task LoadFromDatabase()
    {
        CalibrationMethods.Clear();
        Source.Clear();
        await LoadCalibrationMethodsAsync();
        await LoadCalibrationsAsync();
    }

    protected override async Task DeleteImplementation(ItemViewModelBase vm)
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");
        await databaseService!.DeleteCalibrationAsync(vm.Id);
    }

    protected override void AddImplementation()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var methodId = CalibrationMethods[0].Id;
            var inputs = new Dictionary<string, double>();
            foreach (var input in CalibrationMethods[0].Properties.Inputs)
            {
                inputs.Add(input, 0.0);
            }

            var calibration = new Calibration(Guid.NewGuid(), "new calibration", methodId, inputs);

            var cvm = new CalibrationViewModel(calibration, CalibrationMethods, false)
            {
                IsDirty = true
            };

            OpenPage(cvm);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not add Calibration: {e.Message}");
        }
    }
}
