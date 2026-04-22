using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class LinearShockStrokeSensorConfigurationViewModel : SensorConfigurationViewModel
{
    private LinearShockStrokeSensorConfiguration sensorConfiguration;

    [ObservableProperty] private double? length;
    [ObservableProperty] private int? resolution;

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

        sensorConfiguration = new LinearShockStrokeSensorConfiguration
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

        var configuration = new LinearShockStrokeSensorConfiguration
        {
            Length = Length.Value,
            Resolution = Resolution.Value
        };

        return SensorConfiguration.ToJson(configuration);
    }

    public LinearShockStrokeSensorConfigurationViewModel() : this(new LinearShockStrokeSensorConfiguration())
    {
    }

    public LinearShockStrokeSensorConfigurationViewModel(LinearShockStrokeSensorConfiguration configuration)
    {
        Type = SensorType.LinearShockStroke;
        sensorConfiguration = configuration;
        Length = configuration.Length;
        Resolution = configuration.Resolution;
    }
}