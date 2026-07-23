using System;
using System.Linq;
using ScottPlot;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.Sessions.Plots;
using Sufni.App.Theming;
namespace Sufni.App.LiveDaq.Plots;

public sealed class FramePitchRollPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    private const string Title = "Frame Pitch/Roll (deg)";
    private const string EmptyMessage = "No frame pitch/roll data";
    private const double AxisFloorDegrees = 5.0;
    private const double AxisPadding = 1.1;

    public void LoadProjection(TelemetryData telemetryData, RecordedImuDisplaySeries displaySeries)
    {
        if (telemetryData.ImuData is null)
        {
            ShowEmptyState(telemetryData.Metadata.Duration);
            return;
        }

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
                    CreatePitchValues(telemetryData.ImuData, pitchRoll),
                    "0.#"),
                new RecordedTimeSeries(
                    "Roll",
                    "deg",
                    RearColor,
                    CreateRollValues(telemetryData.ImuData, pitchRoll),
                    "0.#")
            ],
            new RecordedTimeSeriesValueRange(-maximum, maximum),
            telemetryData,
            [
                RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetryData.Airtimes, PlotTheme),
            ]));
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

    private static RecordedTimeSeriesValues CreatePitchValues(RawImuData imuData, FramePitchRollSeries pitchRoll)
    {
        if (!imuData.HasGaps)
        {
            return new SampledValues(pitchRoll.PitchDegrees, imuData.SampleRate);
        }

        if (pitchRoll.Segments.Count == 0)
        {
            return new ExplicitValues(pitchRoll.Times, pitchRoll.PitchDegrees);
        }

        return new SegmentedValues(
            pitchRoll.Segments
                .Select(segment => new ExplicitValues(segment.Times, segment.PitchDegrees))
                .ToArray());
    }

    private static RecordedTimeSeriesValues CreateRollValues(RawImuData imuData, FramePitchRollSeries pitchRoll)
    {
        if (!imuData.HasGaps)
        {
            return new SampledValues(pitchRoll.RollDegrees, imuData.SampleRate);
        }

        if (pitchRoll.Segments.Count == 0)
        {
            return new ExplicitValues(pitchRoll.Times, pitchRoll.RollDegrees);
        }

        return new SegmentedValues(
            pitchRoll.Segments
                .Select(segment => new ExplicitValues(segment.Times, segment.RollDegrees))
                .ToArray());
    }
}
