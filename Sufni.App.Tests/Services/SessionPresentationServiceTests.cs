using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services;

public class SessionPresentationServiceTests
{
    private readonly SessionPresentationService service = new();

    [Fact]
    public void BuildCachePresentation_FrontOnly_RendersFrontOnlyAndOmitsBalance()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: false);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.NotNull(result.FrontTravelHistogram);
        Assert.NotNull(result.FrontVelocityHistogram);
        Assert.Null(result.RearTravelHistogram);
        Assert.Null(result.RearVelocityHistogram);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
        Assert.NotNull(result.DamperPercentages.FrontHscPercentage);
        Assert.Null(result.DamperPercentages.RearHscPercentage);
    }

    [Fact]
    public void BuildCachePresentation_RearOnly_RendersRearOnlyAndOmitsBalance()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: false, rearPresent: true);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.Null(result.FrontTravelHistogram);
        Assert.Null(result.FrontVelocityHistogram);
        Assert.NotNull(result.RearTravelHistogram);
        Assert.NotNull(result.RearVelocityHistogram);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
        Assert.Null(result.DamperPercentages.FrontHscPercentage);
        Assert.NotNull(result.DamperPercentages.RearHscPercentage);
    }

    [Fact]
    public void BuildCachePresentation_FrontAndRear_RendersBalanceAndPercentages()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.NotNull(result.FrontTravelHistogram);
        Assert.NotNull(result.RearTravelHistogram);
        Assert.NotNull(result.FrontVelocityHistogram);
        Assert.NotNull(result.RearVelocityHistogram);
        Assert.True(result.BalanceAvailable);
        Assert.NotNull(result.CompressionBalance);
        Assert.NotNull(result.ReboundBalance);
        Assert.NotNull(result.DamperPercentages.FrontHscPercentage);
        Assert.NotNull(result.DamperPercentages.RearHscPercentage);
    }

    [Fact]
    public void BuildCachePresentation_InsufficientBalanceSamples_OmitsBalance()
    {
        var telemetry = CreateTelemetryWithSingleBalanceSamplePerSide();

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.NotNull(result.FrontTravelHistogram);
        Assert.NotNull(result.RearTravelHistogram);
        Assert.NotNull(result.FrontVelocityHistogram);
        Assert.NotNull(result.RearVelocityHistogram);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
    }

    [Fact]
    public void BuildCachePresentation_WithoutStrokeData_OmitsHistogramsAndPercentages()
    {
        var telemetry = CreateTelemetryWithoutStrokes();

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.Null(result.FrontTravelHistogram);
        Assert.Null(result.RearTravelHistogram);
        Assert.Null(result.FrontVelocityHistogram);
        Assert.Null(result.RearVelocityHistogram);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
        Assert.Null(result.DamperPercentages.FrontHscPercentage);
        Assert.Null(result.DamperPercentages.RearHscPercentage);
    }

    [Fact]
    public void CalculateDamperPercentages_UsesDampingSpeedCutoffs()
    {
        var telemetry = new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000, Duration = 0.004 },
            Front = CreateDamperSuspension([150, 250, -150, -250]),
            Rear = CreateDamperSuspension([350, 450, -350, -450]),
            Airtimes = [],
            Markers = [],
        };
        var cutoffs = new DampingSpeedCutoffs(
            new DampingSpeedCutoffSide(200, 200),
            new DampingSpeedCutoffSide(400, 400));

        var result = service.CalculateDamperPercentages(telemetry, dampingSpeedCutoffs: cutoffs);

        Assert.Equal(25, result.FrontLscPercentage);
        Assert.Equal(25, result.FrontHscPercentage);
        Assert.Equal(25, result.FrontLsrPercentage);
        Assert.Equal(25, result.FrontHsrPercentage);
        Assert.Equal(25, result.RearLscPercentage);
        Assert.Equal(25, result.RearHscPercentage);
        Assert.Equal(25, result.RearLsrPercentage);
        Assert.Equal(25, result.RearHsrPercentage);
    }

    [Fact]
    public void SessionCachePresentationData_RoundTripsThroughSessionCache()
    {
        var cache = new SessionCache
        {
            SessionId = Guid.NewGuid(),
            FrontTravelHistogram = "front-travel",
            RearTravelHistogram = "rear-travel",
            FrontVelocityHistogram = "front-velocity",
            RearVelocityHistogram = "rear-velocity",
            CompressionBalance = "compression",
            ReboundBalance = "rebound",
            FrontHscPercentage = 1,
            RearHscPercentage = 2,
            FrontLscPercentage = 3,
            RearLscPercentage = 4,
            FrontLsrPercentage = 5,
            RearLsrPercentage = 6,
            FrontHsrPercentage = 7,
            RearHsrPercentage = 8,
        };

        var presentation = SessionCachePresentationData.FromCache(cache);
        var roundTripped = presentation.ToCache(cache.SessionId);

        Assert.True(presentation.BalanceAvailable);
        Assert.Equal(cache.FrontTravelHistogram, roundTripped.FrontTravelHistogram);
        Assert.Equal(cache.RearTravelHistogram, roundTripped.RearTravelHistogram);
        Assert.Equal(cache.FrontVelocityHistogram, roundTripped.FrontVelocityHistogram);
        Assert.Equal(cache.RearVelocityHistogram, roundTripped.RearVelocityHistogram);
        Assert.Equal(cache.CompressionBalance, roundTripped.CompressionBalance);
        Assert.Equal(cache.ReboundBalance, roundTripped.ReboundBalance);
        Assert.Equal(cache.FrontHscPercentage, roundTripped.FrontHscPercentage);
        Assert.Equal(cache.RearHsrPercentage, roundTripped.RearHsrPercentage);
    }

    private static Sufni.Telemetry.TelemetryData CreateTelemetryWithSingleBalanceSamplePerSide()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry.Front.Strokes = new Sufni.Telemetry.Strokes
        {
            Compressions =
            [
                new Sufni.Telemetry.Stroke
                {
                    Start = 0,
                    End = 0,
                    Stat = new Sufni.Telemetry.StrokeStat
                    {
                        Count = 1,
                        MaxTravel = 10,
                        MaxVelocity = 100,
                    },
                    DigitizedTravel = [0],
                    DigitizedVelocity = [0],
                    FineDigitizedVelocity = [0],
                },
            ],
            Rebounds = [],
        };

        telemetry.Rear.Strokes = new Sufni.Telemetry.Strokes
        {
            Compressions =
            [
                new Sufni.Telemetry.Stroke
                {
                    Start = 0,
                    End = 0,
                    Stat = new Sufni.Telemetry.StrokeStat
                    {
                        Count = 1,
                        MaxTravel = 12,
                        MaxVelocity = 120,
                    },
                    DigitizedTravel = [0],
                    DigitizedVelocity = [0],
                    FineDigitizedVelocity = [0],
                },
            ],
            Rebounds = [],
        };

        return telemetry;
    }

    private static Sufni.Telemetry.TelemetryData CreateTelemetryWithoutStrokes()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry.Front.Strokes = new Sufni.Telemetry.Strokes
        {
            Compressions = [],
            Rebounds = [],
        };

        telemetry.Rear.Strokes = new Sufni.Telemetry.Strokes
        {
            Compressions = [],
            Rebounds = [],
        };

        return telemetry;
    }

    private static Suspension CreateDamperSuspension(double[] velocity)
    {
        return new Suspension
        {
            Present = true,
            MaxTravel = 100,
            Travel = [0, 10, 20, 30],
            Velocity = velocity,
            TravelBins = [0, 10, 20, 30, 40],
            VelocityBins = [-500, 0, 500],
            FineVelocityBins = [-500, 0, 500],
            Strokes = new Strokes
            {
                Compressions =
                [
                    new Stroke
                    {
                        Start = 0,
                        End = 1,
                        Stat = new StrokeStat { Count = 2 },
                        DigitizedTravel = [],
                        DigitizedVelocity = [],
                        FineDigitizedVelocity = [],
                    },
                ],
                Rebounds =
                [
                    new Stroke
                    {
                        Start = 2,
                        End = 3,
                        Stat = new StrokeStat { Count = 2 },
                        DigitizedTravel = [],
                        DigitizedVelocity = [],
                        FineDigitizedVelocity = [],
                    },
                ],
            },
        };
    }
}
