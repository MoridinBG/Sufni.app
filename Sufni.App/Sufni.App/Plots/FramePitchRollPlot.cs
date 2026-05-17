using System;
using ScottPlot;
using Sufni.App.Services.Imu;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public sealed class FramePitchRollPlot(Plot plot) : RecordedTimeSeriesPlot(plot)
{
    private const string Title = "Frame Pitch/Roll (deg)";
    private const string EmptyMessage = "No frame pitch/roll data";
    private const double AxisFloorDegrees = 5.0;
    private const double AxisPadding = 1.1;

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (telemetryData.ImuData is null)
        {
            ShowEmptyState(telemetryData.Metadata.Duration);
            return;
        }

        var displaySeries = ImuDisplaySignalProcessor.ProcessRecorded(telemetryData);
        if (displaySeries.FramePitchRoll is not { } pitchRoll || pitchRoll.Times.Length == 0)
        {
            ShowEmptyState(telemetryData.Metadata.Duration);
            return;
        }

        var maximum = CalculateMaximum(pitchRoll);
        LoadTimeSeries(new RecordedTimeSeriesData(
            Title,
            EmptyMessage,
            telemetryData.Metadata.Duration,
            [
                new RecordedTimeSeries(
                    "Pitch",
                    "deg",
                    FrontColor,
                    new SampledValues(pitchRoll.PitchDegrees, telemetryData.ImuData.SampleRate),
                    "0.#"),
                new RecordedTimeSeries(
                    "Roll",
                    "deg",
                    RearColor,
                    new SampledValues(pitchRoll.RollDegrees, telemetryData.ImuData.SampleRate),
                    "0.#")
            ],
            new RecordedTimeSeriesValueRange(-maximum, maximum),
            telemetryData));
    }

    private void ShowEmptyState(double durationSeconds)
    {
        LoadTimeSeries(new RecordedTimeSeriesData(
            Title,
            EmptyMessage,
            durationSeconds,
            []));
    }

    private static double CalculateMaximum(FramePitchRollSeries pitchRoll)
    {
        var maximum = 0.0;
        for (var i = 0; i < pitchRoll.PitchDegrees.Length; i++)
        {
            maximum = Math.Max(maximum, Math.Abs(pitchRoll.PitchDegrees[i]));
        }

        for (var i = 0; i < pitchRoll.RollDegrees.Length; i++)
        {
            maximum = Math.Max(maximum, Math.Abs(pitchRoll.RollDegrees[i]));
        }

        return Math.Max(AxisFloorDegrees, maximum * AxisPadding);
    }
}
