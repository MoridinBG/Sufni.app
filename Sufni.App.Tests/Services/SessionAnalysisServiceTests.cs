using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services;

public class SessionAnalysisServiceTests
{
    private readonly SessionAnalysisService service = new();

    [Fact]
    public void Analyze_ReturnsHidden_WhenTelemetryIsMissing()
    {
        var result = service.Analyze(CreateRequest(null));

        Assert.Equal(SurfacePresentationState.Hidden, result.State);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Analyze_EmitsFullSessionWarning_WhenRangeIsNotSelected()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, range: null));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.DataQuality &&
            finding.Severity == SessionAnalysisSeverity.Watch &&
            finding.Evidence.Any(evidence => evidence.Value == "Full session"));
    }

    [Fact]
    public void Analyze_SkipsBalanceFindings_WhenOneSuspensionEndIsMissing()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: null);

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.DoesNotContain(result.Findings, finding => finding.Category == SessionAnalysisCategory.Balance);
        Assert.Contains(result.Findings, finding => finding.Category == SessionAnalysisCategory.DataQuality);
    }

    [Fact]
    public void Analyze_EmitsTravelUseFindings_ForShallowTravelAndRepeatedBottomouts()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 30),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62, bottomouts: 4));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.TravelUse &&
            finding.Severity == SessionAnalysisSeverity.Watch);
        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.TravelUse &&
            finding.Severity == SessionAnalysisSeverity.Watch &&
            finding.Evidence.Any(evidence => evidence.Label == "Stroke bottomouts" && evidence.Value == "4"));
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.TravelUse &&
            finding.Severity == SessionAnalysisSeverity.Action);
    }

    [Fact]
    public void Analyze_EscalatesBottomoutFindings_WhenBottomoutsAreChronic()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62, bottomouts: 6));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.TravelUse &&
            finding.Severity == SessionAnalysisSeverity.Action &&
            finding.Evidence.Any(evidence => evidence.Label == "Stroke bottomouts" && evidence.Value == "6"));
    }

    [Fact]
    public void Analyze_AttributesShallowSlowCompressionToPacking_AndSuppressesDuplicateSideDamping()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 50, averageTravelPercent: 30, compressionBaseSpeed: 1000, reboundBaseSpeed: 1900),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionAnalysisTargetProfile.Enduro));

        Assert.Contains(result.Findings, finding => finding.Category == SessionAnalysisCategory.Packing);
        Assert.DoesNotContain(result.Findings, finding => finding.Category == SessionAnalysisCategory.ForkDamping);
    }

    [Fact]
    public void Analyze_AttributesDeepSlowReboundToPacking()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 82, averageTravelPercent: 58, compressionBaseSpeed: 3600, reboundBaseSpeed: 900, reboundSlope: 5),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionAnalysisTargetProfile.Enduro));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.Packing &&
            finding.Severity == SessionAnalysisSeverity.Action &&
            finding.Evidence.Any(evidence => evidence.Label == "Rebound p95"));
    }

    [Fact]
    public void Analyze_PrioritizesSupport_WhenDeepTravelHasRepeatedBottomouts()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 99, averageTravelPercent: 65, compressionBaseSpeed: 4200, reboundBaseSpeed: 900, bottomouts: 6),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionAnalysisTargetProfile.Enduro));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.Packing &&
            finding.Severity == SessionAnalysisSeverity.Action &&
            finding.Evidence.Any(evidence => evidence.Label == "Stroke bottomouts" && evidence.Value == "6"));
    }

    [Fact]
    public void Analyze_UsesSelectedModesProfileAndRequestDamperPercentages_AsEvidence()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 80, averageTravelPercent: 35, compressionBaseSpeed: 3000, reboundBaseSpeed: 1000),
            rear: BuildSide());
        var damperPercentages = new SessionDamperPercentages(11, 22, 33, 44, 55, 66, 77, 88);

        var result = service.Analyze(CreateRequest(
            telemetry,
            SelectedRange,
            TravelHistogramMode.DynamicSag,
            VelocityAverageMode.StrokePeakAveraged,
            BalanceDisplacementMode.Travel,
            SessionAnalysisTargetProfile.DH,
            damperPercentages));

        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Dynamic sag travel stats");
        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Stroke-peak average velocity");
        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "DH profile");
        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Current damper band percentages" && evidence.Value == "11.0");
    }

    [Fact]
    public void Analyze_EmitsBalanceFindings_WhenSlopeDeltaExceedsThresholds()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(compressionSlope: 55, reboundSlope: 32),
            rear: BuildSide(compressionSlope: 28, reboundSlope: 15));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.Balance &&
            finding.Evidence.Any(evidence => evidence.Label == "Slope delta"));
    }

    [Fact]
    public void Analyze_ReportsLimitedBalanceContext_WhenBalancedDataIsShallowOrSlow()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 28, compressionBaseSpeed: 1200, reboundBaseSpeed: 900),
            rear: BuildSide(maxTravelPercent: 54, averageTravelPercent: 30, compressionBaseSpeed: 1250, reboundBaseSpeed: 950));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.Balance &&
            finding.Severity == SessionAnalysisSeverity.Info &&
            finding.Evidence.Any(evidence => evidence.Label == "Context limit"));
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.Balance &&
            finding.Evidence.Any(evidence => evidence.Label == "Slope delta"));
    }

    [Fact]
    public void Analyze_DoesNotEmitBalanceFinding_WhenSlopesAreQuietAndDataIsRepresentative()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.DoesNotContain(result.Findings, finding => finding.Category == SessionAnalysisCategory.Balance);
    }

    [Fact]
    public void Analyze_EmitsLimitedBalanceContextAlongsideSlopeFinding_WhenDataIsTameAndSlopesDiverge()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 45, compressionSlope: 70, compressionBaseSpeed: 2500),
            rear: BuildSide(maxTravelPercent: 45, compressionSlope: 10, compressionBaseSpeed: 2500));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.Balance &&
            finding.Evidence.Any(evidence => evidence.Label == "Slope delta"));
        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionAnalysisCategory.Balance &&
            finding.Severity == SessionAnalysisSeverity.Info &&
            finding.Evidence.Any(evidence => evidence.Label == "Context limit"));
    }

    [Fact]
    public void Analyze_DoesNotUseVibrationRecommendation_ForMixedTravelImuPairing()
    {
        var telemetry = CreateTelemetry(front: null, rear: BuildSide(), imuLocations: [(byte)ImuLocation.Fork]);

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding => finding.Category == SessionAnalysisCategory.Vibration);
        Assert.DoesNotContain(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Comparable vibration");
    }

    private static readonly TelemetryTimeRange SelectedRange = new(0.1, 8.0);

    private static SessionAnalysisRequest CreateRequest(
        TelemetryData? telemetryData,
        TelemetryTimeRange? range = null,
        TravelHistogramMode travelMode = TravelHistogramMode.ActiveSuspension,
        VelocityAverageMode velocityMode = VelocityAverageMode.SampleAveraged,
        BalanceDisplacementMode balanceMode = BalanceDisplacementMode.Zenith,
        SessionAnalysisTargetProfile profile = SessionAnalysisTargetProfile.Trail,
        SessionDamperPercentages? damperPercentages = null)
    {
        return new SessionAnalysisRequest(
            telemetryData,
            range,
            travelMode,
            velocityMode,
            balanceMode,
            damperPercentages ?? new SessionDamperPercentages(null, null, null, null, null, null, null, null),
            profile);
    }

    private static SideSpec BuildSide(
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

    private static TelemetryData CreateTelemetry(
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

    private readonly record struct SideSpec(
        double MaxTravel,
        double MaxTravelPercent,
        double AverageTravelPercent,
        double CompressionBaseSpeed,
        double ReboundBaseSpeed,
        double CompressionSlope,
        double ReboundSlope,
        int Bottomouts);
}
