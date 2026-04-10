using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class LinearForkSensorConfigurationViewModel : SensorConfigurationViewModel
{
    private LinearForkSensorConfiguration sensorConfiguration;

    #region Observable properties

    [ObservableProperty] private double? length;
    [ObservableProperty] private int? resolution;

    #endregion Observable properties

    #region SensorConfigurationViewModel overrides

    public override void EvaluateDirtiness()
    {
        IsDirty = !MathUtils.AreEqual(Length, sensorConfiguration.Length) ||
                  Resolution != sensorConfiguration.Resolution;
    }

    public override bool CanSave()
    {
        return Length is not null && Resolution is not null;
    }

    public override void Save()
    {
        Debug.Assert(Length.HasValue);
        Debug.Assert(Resolution.HasValue);

        sensorConfiguration = new LinearForkSensorConfiguration
        {
            Length = Length.Value,
            Resolution = Resolution.Value
        };

        EvaluateDirtiness();
    }

    public override string ToJson()
    {
        Debug.Assert(Length is not null);
        Debug.Assert(Resolution is not null);

        var sc = new LinearForkSensorConfiguration
        {
            Length = Length.Value,
            Resolution = Resolution.Value
        };

        return SensorConfiguration.ToJson(sc);
    }

    #endregion SensorConfigurationViewModel overrides

    #region Constructors

    public LinearForkSensorConfigurationViewModel() : this(new LinearForkSensorConfiguration()) { }

    public LinearForkSensorConfigurationViewModel(LinearForkSensorConfiguration configuration)
    {
        Type = SensorType.LinearFork;
        sensorConfiguration = configuration;
        Length = sensorConfiguration.Length;
        Resolution = sensorConfiguration.Resolution;
    }

    #endregion Constructors
}