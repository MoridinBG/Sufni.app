using Sufni.App.Setups.Coordinators;

namespace Sufni.App.Setups.ViewModels.SensorConfigurations;

public partial class LinearForkSensorConfigurationViewModel : LinearSensorConfigurationViewModelBase
{
    public LinearForkSensorConfigurationViewModel(SensorDraft.LinearFork draft)
    {
        Type = draft.Type;
        Length = draft.Length;
        Resolution = draft.Resolution;
    }

    public override SensorDraft CaptureDraft() => new SensorDraft.LinearFork(Length, Resolution);
}
