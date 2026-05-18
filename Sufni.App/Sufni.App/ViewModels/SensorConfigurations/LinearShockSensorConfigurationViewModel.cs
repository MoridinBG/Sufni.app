using System.Diagnostics;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class LinearShockSensorConfigurationViewModel : LinearSensorConfigurationViewModelBase
{
    private LinearShockSensorConfiguration sensorConfiguration;

    #region SensorConfigurationViewModel overrides

    public override void Save()
    {
        Debug.Assert(Length.HasValue);
        Debug.Assert(Resolution.HasValue);

        sensorConfiguration = new LinearShockSensorConfiguration
        {
            Length = Length.Value,
            Resolution = Resolution.Value,
            Type = Type,
        };

        AcceptSavedLinearValues(sensorConfiguration.Length, sensorConfiguration.Resolution);
    }

    public override string ToJson()
    {
        Debug.Assert(Length is not null);
        Debug.Assert(Resolution is not null);

        var sc = new LinearShockSensorConfiguration
        {
            Length = Length.Value,
            Resolution = Resolution.Value,
            Type = Type,
        };

        return SensorConfiguration.ToJson(sc);
    }

    #endregion SensorConfigurationViewModel overrides

    #region Constructors

    public LinearShockSensorConfigurationViewModel() : this(new LinearShockSensorConfiguration()) { }

    public LinearShockSensorConfigurationViewModel(SensorType type)
        : this(new LinearShockSensorConfiguration { Type = type }) { }

    public LinearShockSensorConfigurationViewModel(LinearShockSensorConfiguration configuration)
    {
        Type = configuration.Type is SensorType.LinearShockStroke
            ? SensorType.LinearShockStroke
            : SensorType.LinearShock;
        sensorConfiguration = configuration;
        LoadLinearValues(configuration.Length, configuration.Resolution);
    }

    #endregion Constructors
}
