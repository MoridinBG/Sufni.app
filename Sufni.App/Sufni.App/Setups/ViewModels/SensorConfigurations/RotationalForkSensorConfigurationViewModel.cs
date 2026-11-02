using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Setups.Coordinators;

namespace Sufni.App.Setups.ViewModels.SensorConfigurations;

public partial class RotationalForkSensorConfigurationViewModel : SensorConfigurationViewModel
{
    [ObservableProperty] public partial double? MaxLength { get; set; }
    [ObservableProperty] public partial double? ArmLength { get; set; }

    public RotationalForkSensorConfigurationViewModel(SensorDraft.RotationalFork draft)
    {
        Type = draft.Type;
        MaxLength = draft.MaxLength;
        ArmLength = draft.ArmLength;
    }

    public override SensorDraft CaptureDraft() => new SensorDraft.RotationalFork(MaxLength, ArmLength);
}
