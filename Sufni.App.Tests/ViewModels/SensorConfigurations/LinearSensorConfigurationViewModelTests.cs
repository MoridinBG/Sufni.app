using Sufni.App.Models.SensorConfigurations;
using Sufni.App.ViewModels.SensorConfigurations;
using Xunit;

namespace Sufni.App.Tests.ViewModels.SensorConfigurations;

public class LinearSensorConfigurationViewModelTests
{
    [Fact]
    public void LinearForkSensorConfigurationViewModel_Save_UpdatesBaselineAndJson()
    {
        var viewModel = new LinearForkSensorConfigurationViewModel(new LinearForkSensorConfiguration
        {
            Length = 170,
            Resolution = 12
        });

        Assert.False(viewModel.IsDirty);
        Assert.True(viewModel.CanSave());

        viewModel.Length = 175;
        viewModel.Resolution = null;

        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanSave());

        viewModel.Resolution = 14;
        viewModel.Save();

        Assert.False(viewModel.IsDirty);

        var configuration = Assert.IsType<LinearForkSensorConfiguration>(
            SensorConfiguration.FromJson(viewModel.ToJson()));
        Assert.Equal(SensorType.LinearFork, configuration.Type);
        Assert.Equal(175, configuration.Length);
        Assert.Equal(14, configuration.Resolution);
    }

    [Fact]
    public void LinearShockSensorConfigurationViewModel_Save_PreservesShockStrokeTypeAndUpdatesBaseline()
    {
        var viewModel = new LinearShockSensorConfigurationViewModel(new LinearShockSensorConfiguration
        {
            Type = SensorType.LinearShockStroke,
            Length = 190,
            Resolution = 16
        });

        Assert.Equal(SensorType.LinearShockStroke, viewModel.Type);
        Assert.False(viewModel.IsDirty);
        Assert.True(viewModel.CanSave());

        viewModel.Length = 195;
        viewModel.Resolution = 18;
        viewModel.Save();

        Assert.False(viewModel.IsDirty);

        var configuration = Assert.IsType<LinearShockSensorConfiguration>(
            SensorConfiguration.FromJson(viewModel.ToJson()));
        Assert.Equal(SensorType.LinearShockStroke, configuration.Type);
        Assert.Equal(195, configuration.Length);
        Assert.Equal(18, configuration.Resolution);
    }
}
