using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.SessionPages;

namespace Sufni.App.Tests.Views.SessionPages;

public class PreferencesPageViewTests
{
    [AvaloniaFact]
    public async Task PreferencesPageView_BindsPlotSelectionAndAvailability()
    {
        var viewModel = new PreferencesPageViewModel();
        viewModel.ApplyPlotPreferences(new SessionPlotPreferences(
            Travel: false,
            Velocity: true,
            Imu: false,
            PitchRoll: false,
            Speed: true,
            Elevation: false,
            TravelSmoothing: PlotSmoothingLevel.Light,
            VelocitySmoothing: PlotSmoothingLevel.Strong,
            ImuSmoothing: PlotSmoothingLevel.Off,
            PitchRollSmoothing: PlotSmoothingLevel.Light,
            SpeedSmoothing: PlotSmoothingLevel.Strong,
            ElevationSmoothing: PlotSmoothingLevel.Light));
        viewModel.ApplyPlotAvailability(
            travelAvailable: true,
            velocityAvailable: false,
            imuAvailable: true,
            pitchRollAvailable: false,
            speedAvailable: true,
            elevationAvailable: false);

        await using var mounted = await MountAsync(viewModel);

        var travelCheckBox = mounted.View.FindControl<CheckBox>("TravelPlotCheckBox");
        var travelSmoothingComboBox = mounted.View.FindControl<ComboBox>("TravelPlotSmoothingComboBox");

        Assert.NotNull(travelCheckBox);
        Assert.NotNull(travelSmoothingComboBox);
        Assert.False(travelCheckBox!.IsChecked);
        Assert.True(travelCheckBox.IsEnabled);
        Assert.Equal(PlotSmoothingLevel.Light, travelSmoothingComboBox!.SelectedValue);
        Assert.True(travelSmoothingComboBox.IsEnabled);

        travelCheckBox.IsChecked = true;
        travelSmoothingComboBox.SelectedValue = PlotSmoothingLevel.Strong;
        await ViewTestHelpers.FlushDispatcherAsync();

        var preferences = viewModel.CreatePlotPreferences();
        Assert.True(preferences.Travel);
        Assert.Equal(PlotSmoothingLevel.Strong, preferences.TravelSmoothing);
    }

    private static async Task<MountedPreferencesPageView> MountAsync(PreferencesPageViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new PreferencesPageView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedPreferencesPageView(host, view);
    }
}

internal sealed class MountedPreferencesPageView(Window host, PreferencesPageView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public PreferencesPageView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
