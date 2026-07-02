using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public sealed record SignalSmoothingOption(PlotSmoothingLevel Value, string DisplayName);

public sealed partial class SignalPreferenceItemViewModel(string displayName) : ObservableObject
{
    public string DisplayName { get; } = displayName;

    [ObservableProperty] public partial bool Selected { get; set; } = true;
    [ObservableProperty] public partial bool Available { get; set; }
    [ObservableProperty] public partial PlotSmoothingLevel SelectedSmoothing { get; set; } = PlotSmoothingLevel.Off;
}

public sealed class PreferencesPageViewModel : PageViewModelBase
{
    private int committedVelocityFilterWindowMilliseconds = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds;

    public SignalPreferenceItemViewModel TravelSignal { get; } = new("Travel");
    public SignalPreferenceItemViewModel VelocitySignal { get; } = new("Velocity");
    public SignalPreferenceItemViewModel ImuSignal { get; } = new("Vibration RMS");
    public SignalPreferenceItemViewModel PitchRollSignal { get; } = new("Frame pitch/roll");
    public SignalPreferenceItemViewModel SpeedSignal { get; } = new("GPS speed");
    public SignalPreferenceItemViewModel ElevationSignal { get; } = new("Elevation");
    public IReadOnlyList<SignalSmoothingOption> SmoothingOptions { get; } =
    [
        new(PlotSmoothingLevel.Off, "Off"),
        new(PlotSmoothingLevel.Light, "Light"),
        new(PlotSmoothingLevel.Strong, "Strong"),
    ];

    public event EventHandler? ProcessingPreferenceChangeCommitted;

    public int MinVelocityFilterWindowMilliseconds => TelemetryProcessingOptions.MinVelocityFilterWindowMilliseconds;
    public int MaxVelocityFilterWindowMilliseconds => TelemetryProcessingOptions.MaxVelocityFilterWindowMilliseconds;

    public double VelocityFilterWindowMilliseconds
    {
        get => field;
        set
        {
            var clamped = Math.Clamp(
                Math.Round(value),
                TelemetryProcessingOptions.MinVelocityFilterWindowMilliseconds,
                TelemetryProcessingOptions.MaxVelocityFilterWindowMilliseconds);
            if (SetProperty(ref field, clamped))
            {
                OnPropertyChanged(nameof(VelocityFilterWindowDisplay));
            }
        }
    } = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds;

    public string VelocityFilterWindowDisplay
    {
        get
        {
            var milliseconds = CreateProcessingPreferences().VelocityFilterWindowMilliseconds;
            return milliseconds == 0 ? "No filter" : $"{milliseconds} ms";
        }
    }

    public PreferencesPageViewModel()
        : base("Preferences")
    {
    }

    public SignalDisplayPreferences CreateSignalDisplayPreferences()
    {
        return new SignalDisplayPreferences(
            Travel: TravelSignal.Selected,
            Velocity: VelocitySignal.Selected,
            Imu: ImuSignal.Selected,
            PitchRoll: PitchRollSignal.Selected,
            TravelSmoothing: TravelSignal.SelectedSmoothing,
            VelocitySmoothing: VelocitySignal.SelectedSmoothing,
            ImuSmoothing: ImuSignal.SelectedSmoothing,
            PitchRollSmoothing: PitchRollSignal.SelectedSmoothing,
            Speed: SpeedSignal.Selected,
            Elevation: ElevationSignal.Selected,
            SpeedSmoothing: SpeedSignal.SelectedSmoothing,
            ElevationSmoothing: ElevationSignal.SelectedSmoothing);
    }

    public SessionProcessingPreferences CreateProcessingPreferences()
    {
        return new SessionProcessingPreferences((int)Math.Round(VelocityFilterWindowMilliseconds));
    }

    public void ApplyProcessingPreferences(SessionProcessingPreferences preferences)
    {
        var windowMilliseconds = preferences.ToTelemetryProcessingOptions().ClampedVelocityFilterWindowMilliseconds;
        VelocityFilterWindowMilliseconds = windowMilliseconds;
        committedVelocityFilterWindowMilliseconds = windowMilliseconds;
    }

    public void CommitProcessingPreferenceChange()
    {
        var preferences = CreateProcessingPreferences();
        if (preferences.VelocityFilterWindowMilliseconds == committedVelocityFilterWindowMilliseconds)
        {
            return;
        }

        committedVelocityFilterWindowMilliseconds = preferences.VelocityFilterWindowMilliseconds;
        ProcessingPreferenceChangeCommitted?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySignalDisplayPreferences(SignalDisplayPreferences preferences)
    {
        TravelSignal.Selected = preferences.Travel;
        VelocitySignal.Selected = preferences.Velocity;
        ImuSignal.Selected = preferences.Imu;
        PitchRollSignal.Selected = preferences.PitchRoll;
        SpeedSignal.Selected = preferences.Speed;
        ElevationSignal.Selected = preferences.Elevation;
        TravelSignal.SelectedSmoothing = preferences.TravelSmoothing;
        VelocitySignal.SelectedSmoothing = preferences.VelocitySmoothing;
        ImuSignal.SelectedSmoothing = preferences.ImuSmoothing;
        PitchRollSignal.SelectedSmoothing = preferences.PitchRollSmoothing;
        SpeedSignal.SelectedSmoothing = preferences.SpeedSmoothing;
        ElevationSignal.SelectedSmoothing = preferences.ElevationSmoothing;
    }

    public void ApplySignalAvailability(
        bool travelAvailable,
        bool velocityAvailable,
        bool imuAvailable,
        bool pitchRollAvailable,
        bool speedAvailable,
        bool elevationAvailable)
    {
        TravelSignal.Available = travelAvailable;
        VelocitySignal.Available = velocityAvailable;
        ImuSignal.Available = imuAvailable;
        PitchRollSignal.Available = pitchRollAvailable;
        SpeedSignal.Available = speedAvailable;
        ElevationSignal.Available = elevationAvailable;
    }
}
