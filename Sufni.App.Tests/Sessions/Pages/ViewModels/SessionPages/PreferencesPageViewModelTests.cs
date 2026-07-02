using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Tests.Sessions.Pages.ViewModels.SessionPages;

public class PreferencesPageViewModelTests
{
    [Fact]
    public void CreateSignalDisplayPreferences_RoundTripsPitchRollSelectionAndSmoothing()
    {
        var viewModel = new PreferencesPageViewModel();

        viewModel.ApplySignalDisplayPreferences(new SignalDisplayPreferences(
            Travel: true,
            Velocity: true,
            Imu: true,
            PitchRoll: false,
            PitchRollSmoothing: PlotSmoothingLevel.Strong));
        viewModel.ApplySignalAvailability(
            travelAvailable: true,
            velocityAvailable: true,
            imuAvailable: true,
            pitchRollAvailable: false,
            speedAvailable: true,
            elevationAvailable: true);

        var preferences = viewModel.CreateSignalDisplayPreferences();

        Assert.False(viewModel.PitchRollSignal.Selected);
        Assert.False(viewModel.PitchRollSignal.Available);
        Assert.Equal(PlotSmoothingLevel.Strong, viewModel.PitchRollSignal.SelectedSmoothing);
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
