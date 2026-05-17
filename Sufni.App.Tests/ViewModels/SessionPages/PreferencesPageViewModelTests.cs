using Sufni.App.Models;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.SessionPages;

public class PreferencesPageViewModelTests
{
    [Fact]
    public void CreatePlotPreferences_RoundTripsPitchRollSelectionAndSmoothing()
    {
        var viewModel = new PreferencesPageViewModel();

        viewModel.ApplyPlotPreferences(new SessionPlotPreferences(
            Travel: true,
            Velocity: true,
            Imu: true,
            PitchRoll: false,
            PitchRollSmoothing: PlotSmoothingLevel.Strong));
        viewModel.ApplyPlotAvailability(
            travelAvailable: true,
            velocityAvailable: true,
            imuAvailable: true,
            pitchRollAvailable: false,
            speedAvailable: true,
            elevationAvailable: true);

        var preferences = viewModel.CreatePlotPreferences();

        Assert.False(viewModel.PitchRollPlot.Selected);
        Assert.False(viewModel.PitchRollPlot.Available);
        Assert.Equal(PlotSmoothingLevel.Strong, viewModel.PitchRollPlot.SelectedSmoothing);
        Assert.False(preferences.PitchRoll);
        Assert.Equal(PlotSmoothingLevel.Strong, preferences.PitchRollSmoothing);
    }

    [Fact]
    public void ApplyProcessingPreferences_ClampsSliderValueAndUpdatesDisplay()
    {
        var viewModel = new PreferencesPageViewModel();

        viewModel.ApplyProcessingPreferences(new SessionProcessingPreferences(1_500));

        Assert.Equal(TelemetryProcessingOptions.MaxVelocityFilterWindowMilliseconds, viewModel.VelocityFilterWindowMilliseconds);
        Assert.Equal("1000 ms", viewModel.VelocityFilterWindowDisplay);
    }

    [Fact]
    public void CommitProcessingPreferenceChange_RaisesOnlyWhenValueChangedSinceLastCommit()
    {
        var viewModel = new PreferencesPageViewModel();
        var commitCount = 0;
        viewModel.ProcessingPreferenceChangeCommitted += (_, _) => commitCount++;

        viewModel.VelocityFilterWindowMilliseconds = 0;
        viewModel.CommitProcessingPreferenceChange();
        viewModel.CommitProcessingPreferenceChange();

        Assert.Equal(1, commitCount);
        Assert.Equal("No filter", viewModel.VelocityFilterWindowDisplay);
        Assert.Equal(
            TelemetryProcessingOptions.MinVelocityFilterWindowMilliseconds,
            viewModel.CreateProcessingPreferences().VelocityFilterWindowMilliseconds);
    }
}
