using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public sealed record SignalSmoothingOption(PlotSmoothingLevel Value, string DisplayName, string Description);

public sealed partial class SignalPreferenceItemViewModel(string displayName) : ObservableObject
{
    public string DisplayName { get; } = displayName;

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
        new(PlotSmoothingLevel.Off, "Off", "Shows the raw signal with no smoothing."),
        new(PlotSmoothingLevel.Light, "Light", "Trims fine jitter while keeping quick movements visible."),
        new(PlotSmoothingLevel.Strong, "Strong", "Removes more noise but softens sharp, fast movements."),
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

    // Sample rate (Hz) of the loaded recording, used to express the filter
    // window in samples. 0 when unknown (e.g. before telemetry is applied).
    public int SampleRate
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(VelocityFilterWindowDisplay));
            }
        }
    }

    public string VelocityFilterWindowDisplay
    {
        get
        {
            var options = CreateProcessingPreferences().ToTelemetryProcessingOptions();
            var milliseconds = options.ClampedVelocityFilterWindowMilliseconds;
            if (milliseconds == 0)
            {
                return "No filter";
            }

            // Show the effective window in samples when the recording's sample
            // rate is known; fall back to milliseconds otherwise.
            var samples = options.VelocityFilterWindowSamples(SampleRate);
            return samples > 0
                ? $"{samples} samples ({milliseconds} ms)"
                : $"{milliseconds} ms";
        }
    }

    public PreferencesPageViewModel()
        : base("Preferences")
    {
    }

    public SignalDisplayPreferences CreateSignalDisplayPreferences()
    {
        // Signals are always shown; hiding is done by collapsing rows in the
        // signals view, so the per-signal visibility flags stay true here.
        return new SignalDisplayPreferences(
            Travel: true,
            Velocity: true,
            Imu: true,
            PitchRoll: true,
            TravelSmoothing: TravelSignal.SelectedSmoothing,
            VelocitySmoothing: VelocitySignal.SelectedSmoothing,
            ImuSmoothing: ImuSignal.SelectedSmoothing,
            PitchRollSmoothing: PitchRollSignal.SelectedSmoothing,
            Speed: true,
            Elevation: true,
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
