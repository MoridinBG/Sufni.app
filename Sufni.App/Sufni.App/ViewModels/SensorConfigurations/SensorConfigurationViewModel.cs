using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.ViewModels.LinkageParts;

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

    public static SensorConfigurationViewModel? Create(SensorType? type, IReadOnlyList<JointViewModel>? joints = null)
    {
        return type switch
        {
            SensorType.LinearFork => new LinearForkSensorConfigurationViewModel(),
            SensorType.RotationalFork => new RotationalForkSensorConfigurationViewModel(),
            SensorType.LinearShock => new LinearShockSensorConfigurationViewModel(),
            SensorType.LinearShockStroke => new LinearShockSensorConfigurationViewModel(SensorType.LinearShockStroke),
            SensorType.RotationalShock => new RotationalShockSensorConfigurationViewModel(joints),
            _ => null
        };
    }

    public static SensorConfigurationViewModel? FromJson(string? json, IReadOnlyList<JointViewModel>? joints = null)
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
                Debug.Assert(joints is not null);
                vm = new RotationalShockSensorConfigurationViewModel(rssc, joints);
                break;
        }

        return vm;
    }

    #endregion Constructors / Initializers
}