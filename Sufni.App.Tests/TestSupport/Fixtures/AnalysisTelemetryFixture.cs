using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.TestSupport.Fixtures;

/// <summary>
/// Builds analysis-shaped telemetry fixtures: per-side stroke specs become
/// suspension data whose stroke statistics reproduce the spec values, so
/// analysis tests can steer travel use, damping speeds, slopes, and
/// bottomouts directly. Shared by the analysis service and diagnostics tests.
/// </summary>
public static class AnalysisTelemetryFixture
{
    public readonly record struct SideSpec(
        double MaxTravel,
        double MaxTravelPercent,
        double AverageTravelPercent,
        double CompressionBaseSpeed,
        double ReboundBaseSpeed,
        double CompressionSlope,
        double ReboundSlope,
        int Bottomouts);

    public static SessionInsightsRequest CreateRequest(
        TelemetryData? telemetryData,
        TelemetryTimeRange? range = null,
        TravelDistributionMode travelMode = TravelDistributionMode.ActiveSuspension,
        VelocityAverageMode velocityMode = VelocityAverageMode.SampleAveraged,
        BalanceDisplacementMode balanceMode = BalanceDisplacementMode.Zenith,
        BalanceSpeedMode balanceSpeedMode = BalanceSpeedMode.Both,
        SessionInsightsTargetProfile profile = SessionInsightsTargetProfile.Trail,
        SessionDampingPercentages? dampingPercentages = null)
    {
        return new SessionInsightsRequest(
            telemetryData,
            range,
            travelMode,
            velocityMode,
            balanceMode,
            balanceSpeedMode,
            dampingPercentages ?? SessionDampingPercentages.Empty,
            profile);
    }

    public static SideSpec BuildSide(
        double maxTravel = 200,
        double maxTravelPercent = 82,
        double averageTravelPercent = 35,
        double compressionBaseSpeed = 3200,
        double reboundBaseSpeed = 1800,
        double compressionSlope = 36,
        double reboundSlope = 22,
        int bottomouts = 0)
    {
        return new SideSpec(
            maxTravel,
            maxTravelPercent,
            averageTravelPercent,
            compressionBaseSpeed,
            reboundBaseSpeed,
            compressionSlope,
            reboundSlope,
            bottomouts);
    }

    public static TelemetryData CreateTelemetry(
        SideSpec? front,
        SideSpec? rear,
        IReadOnlyList<byte>? imuLocations = null)
    {
        var frontSuspension = front is null ? CreateMissingSuspension() : CreateSuspension(front.Value);
        var rearSuspension = rear is null ? CreateMissingSuspension() : CreateSuspension(rear.Value);
        var sampleCount = Math.Max(frontSuspension.Travel.Length, rearSuspension.Travel.Length);
        var sampleRate = 20;

        return new TelemetryData
        {
            Metadata = new Metadata
            {
                SourceName = "analysis-test.sst",
                Version = 4,
                SampleRate = sampleRate,
                Timestamp = 1_700_000_000,
                Duration = sampleCount / (double)sampleRate,
            },
            Front = frontSuspension,
            Rear = rearSuspension,
            Airtimes = [],
            ImuData = imuLocations is null ? null : CreateImuData(imuLocations),
        };
    }

    private static Suspension CreateMissingSuspension()
    {
        return new Suspension
        {
            Present = false,
            Travel = [],
            Velocity = [],
            Strokes = new Strokes { Compressions = [], Rebounds = [] },
            TravelBins = [],
            VelocityBins = [],
            FineVelocityBins = [],
        };
    }

    private static Suspension CreateSuspension(SideSpec spec)
    {
        var compressionPercents = new[] { 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 75.0, spec.MaxTravelPercent }
            .Select(percent => Math.Min(percent, spec.MaxTravelPercent))
            .ToArray();
        var reboundPercents = new[] { 22.0, 32.0, 42.0, 52.0, 62.0, 72.0, 78.0, spec.MaxTravelPercent }
            .Select(percent => Math.Min(percent, spec.MaxTravelPercent))
            .ToArray();
        var travel = new double[(compressionPercents.Length + reboundPercents.Length) * 2];
        var velocity = new double[travel.Length];
        var compressions = new List<Stroke>();
        var rebounds = new List<Stroke>();
        var index = 0;

        for (var i = 0; i < compressionPercents.Length; i++)
        {
            var travelValue = spec.MaxTravel * compressionPercents[i] / 100.0;
            var speed = Math.Max(spec.CompressionBaseSpeed, spec.CompressionSlope * compressionPercents[i]);
            travel[index] = spec.MaxTravel * spec.AverageTravelPercent / 100.0;
            travel[index + 1] = travelValue;
            velocity[index] = speed;
            velocity[index + 1] = speed;
            compressions.Add(CreateStroke(index, index + 1, travelValue, speed, spec.AverageTravelPercent, spec, i == compressionPercents.Length - 1 ? spec.Bottomouts : 0));
            index += 2;
        }

        for (var i = 0; i < reboundPercents.Length; i++)
        {
            var travelValue = spec.MaxTravel * reboundPercents[i] / 100.0;
            var speed = Math.Max(spec.ReboundBaseSpeed, spec.ReboundSlope * reboundPercents[i]);
            travel[index] = travelValue;
            travel[index + 1] = spec.MaxTravel * spec.AverageTravelPercent / 100.0;
            velocity[index] = -speed;
            velocity[index + 1] = -speed;
            rebounds.Add(CreateStroke(index, index + 1, travelValue, -speed, spec.AverageTravelPercent, spec, 0));
            index += 2;
        }

        return new Suspension
        {
            Present = true,
            MaxTravel = spec.MaxTravel,
            Travel = travel,
            Velocity = velocity,
            Strokes = new Strokes { Compressions = [.. compressions], Rebounds = [.. rebounds] },
            TravelBins = Enumerable.Range(0, 21).Select(i => spec.MaxTravel / 20.0 * i).ToArray(),
            VelocityBins = [],
            FineVelocityBins = [],
        };
    }

    private static Stroke CreateStroke(
        int start,
        int end,
        double maxTravel,
        double maxVelocity,
        double averageTravelPercent,
        SideSpec spec,
        int bottomouts)
    {
        const int count = 2;
        return new Stroke
        {
            Start = start,
            End = end,
            DigitizedTravel = [0, 1],
            DigitizedVelocity = [0, 1],
            FineDigitizedVelocity = [0, 1],
            Stat = new StrokeStat
            {
                SumTravel = spec.MaxTravel * averageTravelPercent / 100.0 * count,
                MaxTravel = maxTravel,
                SumVelocity = maxVelocity * count,
                MaxVelocity = maxVelocity,
                Bottomouts = bottomouts,
                Count = count,
            },
        };
    }

    private static RawImuData CreateImuData(IReadOnlyList<byte> activeLocations)
    {
        return new RawImuData
        {
            SampleRate = 20,
            ActiveLocations = activeLocations.ToList(),
            Meta = activeLocations.Select(location => new ImuMetaEntry(location, 1.0f, 1.0f)).ToList(),
            Records = Enumerable.Range(0, 32)
                .Select(_ => new ImuRecord(0, 0, 2, 0, 0, 0))
                .ToList(),
        };
    }
}
