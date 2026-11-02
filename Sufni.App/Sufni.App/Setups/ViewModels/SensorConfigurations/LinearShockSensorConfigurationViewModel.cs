using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models.SensorConfigurations;

namespace Sufni.App.Setups.ViewModels.SensorConfigurations;

public partial class LinearShockSensorConfigurationViewModel : LinearSensorConfigurationViewModelBase
{
    public LinearShockSensorConfigurationViewModel(SensorDraft.LinearShock draft)
    {
        Type = draft.Type;
        Length = draft.Length;
        Resolution = draft.Resolution;
    }

    public override SensorDraft CaptureDraft() => new SensorDraft.LinearShock(Length, Resolution, Type == SensorType.LinearShockStroke);
}
