using Sufni.App.Models;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.SessionPages;

public class PreferencesPageViewModelTests
{
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
