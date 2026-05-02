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
        viewModel.ApplyPlotPreferences(new SessionPlotPreferences(Travel: false, Velocity: true, Imu: false));
        viewModel.ApplyPlotAvailability(travelAvailable: true, velocityAvailable: false, imuAvailable: true);

        await using var mounted = await MountAsync(viewModel);

        var travelCheckBox = mounted.View.FindControl<CheckBox>("TravelPlotCheckBox");
        var velocityCheckBox = mounted.View.FindControl<CheckBox>("VelocityPlotCheckBox");
        var imuCheckBox = mounted.View.FindControl<CheckBox>("ImuPlotCheckBox");

        Assert.NotNull(travelCheckBox);
        Assert.NotNull(velocityCheckBox);
        Assert.NotNull(imuCheckBox);
        Assert.False(travelCheckBox!.IsChecked);
        Assert.True(travelCheckBox.IsEnabled);
        Assert.True(velocityCheckBox!.IsChecked);
        Assert.False(velocityCheckBox.IsEnabled);
        Assert.False(imuCheckBox!.IsChecked);
        Assert.True(imuCheckBox.IsEnabled);

        travelCheckBox.IsChecked = true;
        imuCheckBox.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(new SessionPlotPreferences(Travel: true, Velocity: true, Imu: true), viewModel.CreatePlotPreferences());
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