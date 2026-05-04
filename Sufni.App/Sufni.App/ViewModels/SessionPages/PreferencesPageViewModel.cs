using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models;

namespace Sufni.App.ViewModels.SessionPages;

public sealed record PlotSmoothingOption(PlotSmoothingLevel Value, string DisplayName);

public sealed partial class PlotPreferenceItemViewModel(string displayName) : ObservableObject
{
    public string DisplayName { get; } = displayName;

    [ObservableProperty] private bool selected = true;
    [ObservableProperty] private bool available;
    [ObservableProperty] private PlotSmoothingLevel selectedSmoothing = PlotSmoothingLevel.Off;
}

public sealed class PreferencesPageViewModel : PageViewModelBase
{
    public PlotPreferenceItemViewModel TravelPlot { get; } = new("Travel");
    public PlotPreferenceItemViewModel VelocityPlot { get; } = new("Velocity");
    public PlotPreferenceItemViewModel ImuPlot { get; } = new("IMU");
    public PlotPreferenceItemViewModel SpeedPlot { get; } = new("Speed");
    public PlotPreferenceItemViewModel ElevationPlot { get; } = new("Elevation");
    public IReadOnlyList<PlotSmoothingOption> SmoothingOptions { get; } =
    [
        new(PlotSmoothingLevel.Off, "Off"),
        new(PlotSmoothingLevel.Light, "Light"),
        new(PlotSmoothingLevel.Strong, "Strong"),
    ];

    public PreferencesPageViewModel()
        : base("Preferences")
    {
    }

    public SessionPlotPreferences CreatePlotPreferences()
    {
        return new SessionPlotPreferences(
            Travel: TravelPlot.Selected,
            Velocity: VelocityPlot.Selected,
            Imu: ImuPlot.Selected,
            TravelSmoothing: TravelPlot.SelectedSmoothing,
            VelocitySmoothing: VelocityPlot.SelectedSmoothing,
            ImuSmoothing: ImuPlot.SelectedSmoothing,
            Speed: SpeedPlot.Selected,
            Elevation: ElevationPlot.Selected,
            SpeedSmoothing: SpeedPlot.SelectedSmoothing,
            ElevationSmoothing: ElevationPlot.SelectedSmoothing);
    }

    public void ApplyPlotPreferences(SessionPlotPreferences preferences)
    {
        TravelPlot.Selected = preferences.Travel;
        VelocityPlot.Selected = preferences.Velocity;
        ImuPlot.Selected = preferences.Imu;
        SpeedPlot.Selected = preferences.Speed;
        ElevationPlot.Selected = preferences.Elevation;
        TravelPlot.SelectedSmoothing = preferences.TravelSmoothing;
        VelocityPlot.SelectedSmoothing = preferences.VelocitySmoothing;
        ImuPlot.SelectedSmoothing = preferences.ImuSmoothing;
        SpeedPlot.SelectedSmoothing = preferences.SpeedSmoothing;
        ElevationPlot.SelectedSmoothing = preferences.ElevationSmoothing;
    }

    public void ApplyPlotAvailability(
        bool travelAvailable,
        bool velocityAvailable,
        bool speedAvailable,
        bool elevationAvailable,
        bool imuAvailable)
    {
        TravelPlot.Available = travelAvailable;
        VelocityPlot.Available = velocityAvailable;
        SpeedPlot.Available = speedAvailable;
        ElevationPlot.Available = elevationAvailable;
        ImuPlot.Available = imuAvailable;
    }
}
