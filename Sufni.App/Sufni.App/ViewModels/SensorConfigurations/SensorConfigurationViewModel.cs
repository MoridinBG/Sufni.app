using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.SensorConfigurations;

public abstract partial class SensorConfigurationViewModel : ViewModelBase
{
    public SensorType Type { get; protected set; }

    #region Observable properties

    [ObservableProperty] private bool isDirty;

    #endregion

    #region Abstract methods

    public abstract bool CanSave();
    public abstract void Save();
    public abstract void EvaluateDirtiness();
    public abstract string ToJson();

    #endregion Abstract methods

    #region Constructors / Initializers

    protected SensorConfigurationViewModel()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsDirty)) return;
            EvaluateDirtiness();
        };
    }

    public static SensorConfigurationViewModel? Create(SensorType? type, BikeViewModel? bike = null)
    {
        return type switch
        {
            SensorType.LinearFork => new LinearForkSensorConfigurationViewModel(),
            SensorType.RotationalFork => new RotationalForkSensorConfigurationViewModel(),
            SensorType.LinearShock => new LinearShockSensorConfigurationViewModel(),
            SensorType.RotationalShock => new RotationalShockSensorConfigurationViewModel(bike),
            _ => null
        };
    }
    
    public static SensorConfigurationViewModel? FromJson(string? json, BikeViewModel? bike = null)
    {
        if (json is null) return null;

        var sc = SensorConfiguration.FromJson(json);
        Debug.Assert(sc is not null);

        SensorConfigurationViewModel? vm = null;
        switch (sc)
        {
            case LinearForkSensorConfiguration lfsc:
                vm = new LinearForkSensorConfigurationViewModel(lfsc);
                break;   
            case LinearShockSensorConfiguration lssc:
                vm = new LinearShockSensorConfigurationViewModel(lssc);
                break;
            case RotationalForkSensorConfiguration rfsc:
                vm = new RotationalForkSensorConfigurationViewModel(rfsc);
                break;
            case RotationalShockSensorConfiguration rssc:
                Debug.Assert(bike is not null);
                vm = new RotationalShockSensorConfigurationViewModel(rssc, bike);
                break;
        }
        
        return vm;
    }

    #endregion Constructors / Initializers

    protected static bool AreEqual(double? a, double? b, double epsilon = 1e-3)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Value - b.Value) < epsilon;
    }
}