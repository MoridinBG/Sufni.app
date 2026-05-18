namespace Sufni.App.ViewModels.SensorConfigurations;

public interface ILinearSensorConfigurationViewModel
{
    double? Length { get; set; }
    int? Resolution { get; set; }
    bool IsDirty { get; }

    bool CanSave();
}
