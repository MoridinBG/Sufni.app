using System.Diagnostics;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class LinearForkSensorConfigurationViewModel : LinearSensorConfigurationViewModelBase
{
    private LinearForkSensorConfiguration sensorConfiguration;

    #region SensorConfigurationViewModel overrides

    public override void Save()
    {
        Debug.Assert(Length.HasValue);
        Debug.Assert(Resolution.HasValue);

        sensorConfiguration = new LinearForkSensorConfiguration
        {
            Length = Length.Value,
            Resolution = Resolution.Value
        };

        AcceptSavedLinearValues(sensorConfiguration.Length, sensorConfiguration.Resolution);
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
        LoadLinearValues(sensorConfiguration.Length, sensorConfiguration.Resolution);
    }

    #endregion Constructors
}
