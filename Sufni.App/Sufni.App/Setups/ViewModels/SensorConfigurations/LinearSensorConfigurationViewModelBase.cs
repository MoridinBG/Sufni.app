using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.Setups.ViewModels.SensorConfigurations;

public abstract partial class LinearSensorConfigurationViewModelBase
    : SensorConfigurationViewModel, ILinearSensorConfigurationViewModel
{
    [ObservableProperty] public partial double? Length { get; set; }
    [ObservableProperty] public partial int? Resolution { get; set; }
}
