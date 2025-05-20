using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class RotationalForkSensorConfigurationViewModel : SensorConfigurationViewModel
{
    private readonly RotationalForkSensorConfiguration sensorConfiguration;

    [ObservableProperty] private double? maxLength;
    [ObservableProperty] private double? armLength;
    
    protected override void EvaluateDirtiness()
    {
        IsDirty = !AreEqual(MaxLength, sensorConfiguration.MaxLength) ||
                  !AreEqual(ArmLength, sensorConfiguration.ArmLength);
    }
    
    public override bool CanSave()
    {
        return MaxLength is not null && ArmLength is not null;
    }

    public RotationalForkSensorConfigurationViewModel() : this(new RotationalForkSensorConfiguration()) { }

    public RotationalForkSensorConfigurationViewModel(RotationalForkSensorConfiguration configuration)
    {
        Type = SensorType.RotationalFork;
        sensorConfiguration = configuration;
        MaxLength = sensorConfiguration.MaxLength;
        ArmLength = sensorConfiguration.ArmLength;
    }
    
    public override string ToJson()
    {
        Debug.Assert(MaxLength is not null);
        Debug.Assert(ArmLength is not null);

        var sc = new RotationalForkSensorConfiguration
        {
            MaxLength = MaxLength.Value,
            ArmLength = ArmLength.Value
        };

        return JsonSerializer.Serialize(sc, SensorConfiguration.SerializerOptions);
    }
}