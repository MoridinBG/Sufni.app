using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.ViewModels.SensorConfigurations;

namespace Sufni.App.Tests.Setups.ViewModels.SensorConfigurations;

public class LinearSensorConfigurationViewModelTests
{
    [Fact]
    public void CaptureDraft_PreservesIncompleteInputWithoutMutatingEarlierCapture()
    {
        var viewModel = new LinearForkSensorConfigurationViewModel(new SensorDraft.LinearFork(170, 12));
        var captured = viewModel.CaptureDraft();
        viewModel.Length = 175;
        viewModel.Resolution = null;

        Assert.Equal(new SensorDraft.LinearFork(170, 12), captured);
        Assert.Equal(new SensorDraft.LinearFork(175, null), viewModel.CaptureDraft());
        Assert.False(SetupSaveRules.IsComplete(viewModel.CaptureDraft()));
        viewModel.Resolution = 14;
        Assert.True(SetupSaveRules.IsComplete(viewModel.CaptureDraft()));
    }

    [Fact]
    public void CaptureDraft_PreservesShockStrokeCalibrationType()
    {
        var viewModel = new LinearShockSensorConfigurationViewModel(new SensorDraft.LinearShock(190, 16, true));
        viewModel.Length = 195;
        viewModel.Resolution = 18;

        var draft = Assert.IsType<SensorDraft.LinearShock>(viewModel.CaptureDraft());
        Assert.Equal(SensorType.LinearShockStroke, draft.Type);
        Assert.Equal(195, draft.Length);
        Assert.Equal(18, draft.Resolution);
    }
}
