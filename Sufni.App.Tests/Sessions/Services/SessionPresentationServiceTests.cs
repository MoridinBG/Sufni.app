using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Services;

public class SessionPresentationServiceTests
{
    private readonly SessionPresentationService service = new();

    [Fact]
    public void BuildCachePresentation_FrontOnly_RendersFrontOnlyAndOmitsBalance()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: false);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.NotNull(result.FrontTravelDistribution);
        Assert.NotNull(result.FrontVelocityDistribution);
        Assert.Null(result.RearTravelDistribution);
        Assert.Null(result.RearVelocityDistribution);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
        Assert.NotNull(result.DampingPercentages.FrontHscPercentage);
        Assert.Null(result.DampingPercentages.RearHscPercentage);
    }

    [Fact]
    public void BuildCachePresentation_RearOnly_RendersRearOnlyAndOmitsBalance()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: false, rearPresent: true);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.Null(result.FrontTravelDistribution);
        Assert.Null(result.FrontVelocityDistribution);
        Assert.NotNull(result.RearTravelDistribution);
        Assert.NotNull(result.RearVelocityDistribution);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
        Assert.Null(result.DampingPercentages.FrontHscPercentage);
        Assert.NotNull(result.DampingPercentages.RearHscPercentage);
    }

    [Fact]
    public void BuildCachePresentation_FrontAndRear_RendersBalanceAndPercentages()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.NotNull(result.FrontTravelDistribution);
        Assert.NotNull(result.RearTravelDistribution);
        Assert.NotNull(result.FrontVelocityDistribution);
        Assert.NotNull(result.RearVelocityDistribution);
        Assert.True(result.BalanceAvailable);
        Assert.NotNull(result.CompressionBalance);
        Assert.NotNull(result.ReboundBalance);
        Assert.NotNull(result.DampingPercentages.FrontHscPercentage);
        Assert.NotNull(result.DampingPercentages.RearHscPercentage);
    }

    [Fact]
    public void BuildCachePresentation_InsufficientBalanceSamples_OmitsBalance()
    {
        var telemetry = CreateTelemetryWithSingleBalanceSamplePerSide();

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.NotNull(result.FrontTravelDistribution);
        Assert.NotNull(result.RearTravelDistribution);
        Assert.NotNull(result.FrontVelocityDistribution);
        Assert.NotNull(result.RearVelocityDistribution);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
    }

    [Fact]
    public void BuildCachePresentation_WithoutStrokeData_OmitsDistributionsAndPercentages()
    {
        var telemetry = CreateTelemetryWithoutStrokes();

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180));

        Assert.Null(result.FrontTravelDistribution);
        Assert.Null(result.RearTravelDistribution);
        Assert.Null(result.FrontVelocityDistribution);
        Assert.Null(result.RearVelocityDistribution);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
        Assert.Null(result.DampingPercentages.FrontHscPercentage);
        Assert.Null(result.DampingPercentages.RearHscPercentage);
    }

    [Fact]
    public void CalculateDampingPercentages_UsesDampingSpeedCutoffs()
    {
        var telemetry = new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000, Duration = 0.004 },
            Front = CreateSuspensionWithVelocity([150, 250, -150, -250]),
            Rear = CreateSuspensionWithVelocity([350, 450, -350, -450]),
            Airtimes = [],
            Markers = [],
        };
        var cutoffs = new DampingSpeedCutoffs(
            new DampingSpeedCutoffSide(200, 200),
            new DampingSpeedCutoffSide(400, 400));

        var result = service.CalculateDampingPercentages(telemetry, dampingSpeedCutoffs: cutoffs);

        Assert.Equal(25, result.FrontLscPercentage);
        Assert.Equal(25, result.FrontHscPercentage);
        Assert.Equal(25, result.FrontLsrPercentage);
        Assert.Equal(25, result.FrontHsrPercentage);
        Assert.Equal(25, result.RearLscPercentage);
        Assert.Equal(25, result.RearHscPercentage);
        Assert.Equal(25, result.RearLsrPercentage);
        Assert.Equal(25, result.RearHsrPercentage);
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

    private static Suspension CreateSuspensionWithVelocity(double[] velocity)
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
