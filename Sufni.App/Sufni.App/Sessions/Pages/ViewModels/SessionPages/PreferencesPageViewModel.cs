using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public sealed record SignalSmoothingOption(PlotSmoothingLevel Value, string DisplayName, string Description);

public sealed partial class SignalPreferenceItemViewModel(string displayName, Func<PlotSmoothingLevel> read, Action<PlotSmoothingLevel> write) : ObservableObject
{
    public string DisplayName { get; } = displayName;

    [ObservableProperty] public partial bool Available { get; set; }
    public PlotSmoothingLevel SelectedSmoothing
    {
        get => read();
        set { if (value != read()) write(value); }
    }

    internal void RefreshSmoothing() => OnPropertyChanged(nameof(SelectedSmoothing));
}

public sealed class PreferencesPageViewModel : PageViewModelBase
{
    private int committedVelocityFilterWindowMilliseconds = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds;

    public SignalPreferenceItemViewModel TravelSignal { get; }
    public SignalPreferenceItemViewModel VelocitySignal { get; }
    public SignalPreferenceItemViewModel ImuSignal { get; }
    public SignalPreferenceItemViewModel PitchRollSignal { get; }
    public SignalPreferenceItemViewModel SpeedSignal { get; }
    public SignalPreferenceItemViewModel ElevationSignal { get; }
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
            var options = CreateProcessingOptions();
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

    private readonly Func<SignalDisplayPreferences> readSignalDisplay;

    public PreferencesPageViewModel(Func<SignalDisplayPreferences> read, Action<SignalDisplayPreferences> write)
        : base("Preferences")
    {
        readSignalDisplay = read;
        TravelSignal = new("Travel", () => read().TravelSmoothing, value => write(CreateSignalDisplayPreferences() with { TravelSmoothing = value }));
        VelocitySignal = new("Velocity", () => read().VelocitySmoothing, value => write(CreateSignalDisplayPreferences() with { VelocitySmoothing = value }));
        ImuSignal = new("Vibration RMS", () => read().ImuSmoothing, value => write(CreateSignalDisplayPreferences() with { ImuSmoothing = value }));
        PitchRollSignal = new("Frame pitch/roll", () => read().PitchRollSmoothing, value => write(CreateSignalDisplayPreferences() with { PitchRollSmoothing = value }));
        SpeedSignal = new("GPS speed", () => read().SpeedSmoothing, value => write(CreateSignalDisplayPreferences() with { SpeedSmoothing = value }));
        ElevationSignal = new("Elevation", () => read().ElevationSmoothing, value => write(CreateSignalDisplayPreferences() with { ElevationSmoothing = value }));
    }

    public SignalDisplayPreferences CreateSignalDisplayPreferences() => readSignalDisplay() with
    {
        Travel = true, Velocity = true, Imu = true, PitchRoll = true, Speed = true, Elevation = true,
    };

    public TelemetryProcessingOptions CreateProcessingOptions()
    {
        return new TelemetryProcessingOptions((int)Math.Round(VelocityFilterWindowMilliseconds));
    }

    public void ApplyProcessingOptions(TelemetryProcessingOptions options)
    {
        var windowMilliseconds = options.ClampedVelocityFilterWindowMilliseconds;
        VelocityFilterWindowMilliseconds = windowMilliseconds;
        committedVelocityFilterWindowMilliseconds = windowMilliseconds;
    }

    public void CommitProcessingPreferenceChange()
    {
        var options = CreateProcessingOptions();
        if (options.ClampedVelocityFilterWindowMilliseconds == committedVelocityFilterWindowMilliseconds)
        {
            return;
        }

        committedVelocityFilterWindowMilliseconds = options.ClampedVelocityFilterWindowMilliseconds;
        ProcessingPreferenceChangeCommitted?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSignalDisplayPreferences(SignalDisplayPreferences previous, SignalDisplayPreferences current)
    {
        if (previous.TravelSmoothing != current.TravelSmoothing) TravelSignal.RefreshSmoothing();
        if (previous.VelocitySmoothing != current.VelocitySmoothing) VelocitySignal.RefreshSmoothing();
        if (previous.ImuSmoothing != current.ImuSmoothing) ImuSignal.RefreshSmoothing();
        if (previous.PitchRollSmoothing != current.PitchRollSmoothing) PitchRollSignal.RefreshSmoothing();
        if (previous.SpeedSmoothing != current.SpeedSmoothing) SpeedSignal.RefreshSmoothing();
        if (previous.ElevationSmoothing != current.ElevationSmoothing) ElevationSignal.RefreshSmoothing();
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
