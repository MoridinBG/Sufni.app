using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class LinearForkSensorConfigurationViewModel : SensorConfigurationViewModel
{
    private readonly LinearForkSensorConfiguration sensorConfiguration;

    [ObservableProperty] private double? length;
    [ObservableProperty] private int? resolution;
    
    protected override void EvaluateDirtiness()
    {
        IsDirty = !AreEqual(Length, sensorConfiguration.Length) ||
                  Resolution != sensorConfiguration.Resolution;
    }

    public override bool CanSave()
    {
        return Length is not null && Resolution is not null;
    }

    public LinearForkSensorConfigurationViewModel() : this(new LinearForkSensorConfiguration()) { }

    public LinearForkSensorConfigurationViewModel(LinearForkSensorConfiguration configuration)
    {
        Type = SensorType.LinearFork;
        sensorConfiguration = configuration;
        Length = sensorConfiguration.Length;
        Resolution = sensorConfiguration.Resolution;
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

        return JsonSerializer.Serialize(sc, SensorConfiguration.SerializerOptions);
    }
}