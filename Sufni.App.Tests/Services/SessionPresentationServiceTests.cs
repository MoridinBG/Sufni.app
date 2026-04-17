using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Services;

public class SessionPresentationServiceTests
{
    private readonly SessionPresentationService service = new();

    [Fact]
    public void BuildCachePresentation_FrontOnly_RendersFrontOnlyAndOmitsBalance()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: false);

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
        var telemetry = TestTelemetryData.Create(frontPresent: false, rearPresent: true);

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
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);

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
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);

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
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);

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
}