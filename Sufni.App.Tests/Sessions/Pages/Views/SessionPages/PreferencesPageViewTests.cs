using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
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
        var viewModel = TestPreferencesPages.Create(new SignalDisplayPreferences(
            TravelSmoothing: PlotSmoothingLevel.Light));
        viewModel.ApplySignalAvailability(
            travelAvailable: true,
            velocityAvailable: false,
            imuAvailable: true,
            pitchRollAvailable: false,
            speedAvailable: true,
            elevationAvailable: false);

        await using var mounted = await MountAsync(viewModel);

        var travelSmoothingComboBox = mounted.View.FindControl<ComboBox>("TravelSignalSmoothingComboBox");

        Assert.NotNull(travelSmoothingComboBox);
        Assert.Equal(PlotSmoothingLevel.Light, travelSmoothingComboBox!.SelectedValue);
        Assert.True(travelSmoothingComboBox.IsEnabled);
        Assert.False(mounted.View.FindControl<ComboBox>("VelocitySignalSmoothingComboBox")!.IsEnabled);

        travelSmoothingComboBox.SelectedValue = PlotSmoothingLevel.Strong;
        await ViewTestHelpers.FlushDispatcherAsync();

        var preferences = viewModel.CreateSignalDisplayPreferences();
        Assert.True(preferences.Travel);
        Assert.Equal(PlotSmoothingLevel.Strong, preferences.TravelSmoothing);
    }

    private static async Task<MountedView<PreferencesPageView>> MountAsync(PreferencesPageViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new PreferencesPageView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedView<PreferencesPageView>(host, view);
    }
}
