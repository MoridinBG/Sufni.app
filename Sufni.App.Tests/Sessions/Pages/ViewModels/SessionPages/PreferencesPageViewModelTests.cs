using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Tests.Sessions.Pages.ViewModels.SessionPages;

public class PreferencesPageViewModelTests
{
    [Fact]
    public void ApplyProcessingPreferences_ClampsSliderValueAndUpdatesDisplay()
    {
        var viewModel = TestPreferencesPages.Create();

        viewModel.ApplyProcessingOptions(new Sufni.Telemetry.TelemetryProcessingOptions(1_500));

        Assert.Equal(TelemetryProcessingOptions.MaxVelocityFilterWindowMilliseconds, viewModel.VelocityFilterWindowMilliseconds);
        Assert.Equal(1000, viewModel.CreateProcessingOptions().VelocityFilterWindowMilliseconds);
    }

    [Fact]
    public void VelocityFilterWindowDisplay_ShowsSampleCount_WhenSampleRateKnown()
    {
        var viewModel = TestPreferencesPages.Create();
        viewModel.SampleRate = 1000;

        viewModel.VelocityFilterWindowMilliseconds = 25;

        Assert.Equal("25 samples (25 ms)", viewModel.VelocityFilterWindowDisplay);
    }

    [Fact]
    public void CommitProcessingPreferenceChange_RaisesOnlyWhenValueChangedSinceLastCommit()
    {
        var viewModel = TestPreferencesPages.Create();
        var committedWindows = new List<int>();
        viewModel.ProcessingPreferenceChangeCommitted += (_, _) =>
            committedWindows.Add(viewModel.CreateProcessingOptions().VelocityFilterWindowMilliseconds);

        viewModel.VelocityFilterWindowMilliseconds = 0;
        viewModel.CommitProcessingPreferenceChange();
        viewModel.CommitProcessingPreferenceChange();

        Assert.Equal([0], committedWindows);
        Assert.Equal(
            TelemetryProcessingOptions.MinVelocityFilterWindowMilliseconds,
            viewModel.CreateProcessingOptions().VelocityFilterWindowMilliseconds);
    }
}
