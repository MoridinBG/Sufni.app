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
            TravelSmoothing: PlotSmoothingLevel.Light,
            VelocitySmoothing: PlotSmoothingLevel.Strong,
            ImuSmoothing: PlotSmoothingLevel.Off));
        viewModel.ApplyPlotAvailability(travelAvailable: true, velocityAvailable: false, imuAvailable: true);

        await using var mounted = await MountAsync(viewModel);

        var travelCheckBox = mounted.View.FindControl<CheckBox>("TravelPlotCheckBox");
        var velocityCheckBox = mounted.View.FindControl<CheckBox>("VelocityPlotCheckBox");
        var imuCheckBox = mounted.View.FindControl<CheckBox>("ImuPlotCheckBox");
        var travelSmoothingComboBox = mounted.View.FindControl<ComboBox>("TravelPlotSmoothingComboBox");
        var velocitySmoothingComboBox = mounted.View.FindControl<ComboBox>("VelocityPlotSmoothingComboBox");
        var imuSmoothingComboBox = mounted.View.FindControl<ComboBox>("ImuPlotSmoothingComboBox");

        Assert.NotNull(travelCheckBox);
        Assert.NotNull(velocityCheckBox);
        Assert.NotNull(imuCheckBox);
        Assert.NotNull(travelSmoothingComboBox);
        Assert.NotNull(velocitySmoothingComboBox);
        Assert.NotNull(imuSmoothingComboBox);
        Assert.False(travelCheckBox!.IsChecked);
        Assert.True(travelCheckBox.IsEnabled);
        Assert.Equal(PlotSmoothingLevel.Light, travelSmoothingComboBox!.SelectedValue);
        Assert.True(travelSmoothingComboBox.IsEnabled);
        Assert.True(velocityCheckBox!.IsChecked);
        Assert.False(velocityCheckBox.IsEnabled);
        Assert.Equal(PlotSmoothingLevel.Strong, velocitySmoothingComboBox!.SelectedValue);
        Assert.False(velocitySmoothingComboBox.IsEnabled);
        Assert.False(imuCheckBox!.IsChecked);
        Assert.True(imuCheckBox.IsEnabled);
        Assert.Equal(PlotSmoothingLevel.Off, imuSmoothingComboBox!.SelectedValue);
        Assert.True(imuSmoothingComboBox.IsEnabled);

        travelCheckBox.IsChecked = true;
        travelSmoothingComboBox.SelectedValue = PlotSmoothingLevel.Strong;
        imuCheckBox.IsChecked = true;
        imuSmoothingComboBox.SelectedValue = PlotSmoothingLevel.Light;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(
            new SessionPlotPreferences(
                Travel: true,
                Velocity: true,
                Imu: true,
                TravelSmoothing: PlotSmoothingLevel.Strong,
                VelocitySmoothing: PlotSmoothingLevel.Strong,
                ImuSmoothing: PlotSmoothingLevel.Light),
            viewModel.CreatePlotPreferences());
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