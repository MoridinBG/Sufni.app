using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models;

namespace Sufni.App.ViewModels.SessionPages;

public sealed partial class PlotPreferenceItemViewModel(string displayName) : ObservableObject
{
    public string DisplayName { get; } = displayName;

    [ObservableProperty] private bool selected = true;
    [ObservableProperty] private bool available;
}

public sealed class PreferencesPageViewModel : PageViewModelBase
{
    public PlotPreferenceItemViewModel TravelPlot { get; } = new("Travel");
    public PlotPreferenceItemViewModel VelocityPlot { get; } = new("Velocity");
    public PlotPreferenceItemViewModel ImuPlot { get; } = new("IMU");

    public PreferencesPageViewModel()
        : base("Preferences")
    {
    }

    public SessionPlotPreferences CreatePlotPreferences()
    {
        return new SessionPlotPreferences(
            TravelPlot.Selected,
            VelocityPlot.Selected,
            ImuPlot.Selected);
    }

    public void ApplyPlotPreferences(SessionPlotPreferences preferences)
    {
        TravelPlot.Selected = preferences.Travel;
        VelocityPlot.Selected = preferences.Velocity;
        ImuPlot.Selected = preferences.Imu;
    }

    public void ApplyPlotAvailability(bool travelAvailable, bool velocityAvailable, bool imuAvailable)
    {
        TravelPlot.Available = travelAvailable;
        VelocityPlot.Available = velocityAvailable;
        ImuPlot.Available = imuAvailable;
    }
}