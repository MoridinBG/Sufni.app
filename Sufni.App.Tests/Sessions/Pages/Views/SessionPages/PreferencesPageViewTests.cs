using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.Views.SessionPages;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Pages.Views.SessionPages;

[Collection("Ui")]
public class PreferencesPageViewTests
{
    [AvaloniaFact]
    public async Task PreferencesPageView_BindsSmoothingAndAvailability()
    {
        var viewModel = new PreferencesPageViewModel();
        viewModel.ApplySignalDisplayPreferences(new SignalDisplayPreferences(
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
        viewModel.ApplySignalAvailability(
            travelAvailable: true,
            velocityAvailable: false,
            imuAvailable: true,
            pitchRollAvailable: false,
            speedAvailable: true,
            elevationAvailable: false);

        await using var mounted = await MountAsync(viewModel);

        var travelLabel = mounted.View.FindControl<TextBlock>("TravelSignalLabel");
        var travelSmoothingComboBox = mounted.View.FindControl<ComboBox>("TravelSignalSmoothingComboBox");

        Assert.NotNull(travelLabel);
        Assert.NotNull(travelSmoothingComboBox);
        Assert.True(travelLabel!.IsEnabled);
        Assert.Equal(PlotSmoothingLevel.Light, travelSmoothingComboBox!.SelectedValue);
        Assert.True(travelSmoothingComboBox.IsEnabled);

        travelSmoothingComboBox.SelectedValue = PlotSmoothingLevel.Strong;
        await ViewTestHelpers.FlushDispatcherAsync();

        var preferences = viewModel.CreateSignalDisplayPreferences();
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
