using System.Xml.Linq;
using Sufni.Telemetry;
using Sufni.App.Bikes.Models;

using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Services;

public class SessionPresentationServiceTests
{
    private readonly ISessionPresentationService service = new SessionPresentationService();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildCachePresentation_SingleSide_RendersValidDistributionAndOmitsBalance(bool front)
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: front, rearPresent: !front);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180), cancellationToken: TestContext.Current.CancellationToken);

        AssertSvg(front ? result.FrontTravelDistribution : result.RearTravelDistribution, 320, 180);
        AssertSvg(front ? result.FrontVelocityDistribution : result.RearVelocityDistribution, 256, 478);
        Assert.Null(front ? result.RearTravelDistribution : result.FrontTravelDistribution);
        Assert.False(result.BalanceAvailable);
        Assert.Null(result.CompressionBalance);
        Assert.Null(result.ReboundBalance);
    }

    [Fact]
    public void BuildCachePresentation_FrontAndRear_RendersBalanceAndPercentages()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.BalanceAvailable);
        AssertSvg(result.CompressionBalance, 320, 180);
        AssertSvg(result.ReboundBalance, 320, 180);
    }

    [Fact]
    public void BuildCachePresentation_InsufficientBalanceSamples_OmitsBalance()
    {
        var telemetry = CreateTelemetryWithSingleBalanceSamplePerSide();

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180), cancellationToken: TestContext.Current.CancellationToken);

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

        var result = service.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180), cancellationToken: TestContext.Current.CancellationToken);

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
            Metadata = new Metadata { SampleRate = 1000, Duration = 0.010 },
            Front = CreateSuspensionWithVelocity([150, 250, 300, -150, -100, -50, -250, -300, -350, -400]),
            Rear = CreateSuspensionWithVelocity([100, 150, 200, 250, 450, 500, 550, -200, -300, -450]),
            Airtimes = [],
            Markers = [],
        };
        var cutoffs = new DampingSpeedCutoffs(
            new DampingSpeedCutoffSide(200, 200),
            new DampingSpeedCutoffSide(400, 400));

        var result = service.CalculateDampingPercentages(telemetry, dampingSpeedCutoffs: cutoffs);

        Assert.Equal(10, result.FrontLscPercentage);
        Assert.Equal(20, result.FrontHscPercentage);
        Assert.Equal(30, result.FrontLsrPercentage);
        Assert.Equal(40, result.FrontHsrPercentage);
        Assert.Equal(40, result.RearLscPercentage);
        Assert.Equal(30, result.RearHscPercentage);
        Assert.Equal(20, result.RearLsrPercentage);
        Assert.Equal(10, result.RearHsrPercentage);
    }

    private static Sufni.Telemetry.TelemetryData CreateTelemetryWithSingleBalanceSamplePerSide()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry = telemetry with { Front = telemetry.Front with { Strokes = new Sufni.Telemetry.Strokes
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
                },
            ],
            Rebounds = [],
        } } };

        telemetry = telemetry with { Rear = telemetry.Rear with { Strokes = new Sufni.Telemetry.Strokes
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
                },
            ],
            Rebounds = [],
        } } };

        return telemetry;
    }

    private static Sufni.Telemetry.TelemetryData CreateTelemetryWithoutStrokes()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry = telemetry with { Front = telemetry.Front with { Strokes = new Sufni.Telemetry.Strokes
        {
            Compressions = [],
            Rebounds = [],
        } } };

        telemetry = telemetry with { Rear = telemetry.Rear with { Strokes = new Sufni.Telemetry.Strokes
        {
            Compressions = [],
            Rebounds = [],
        } } };

        return telemetry;
    }

    private static void AssertSvg(string? xml, int width, int height)
    {
        var svg = XElement.Parse(Assert.IsType<string>(xml));
        Assert.Equal("svg", svg.Name.LocalName);
        Assert.Equal(width.ToString(System.Globalization.CultureInfo.InvariantCulture), svg.Attribute("width")!.Value);
        Assert.Equal(height.ToString(System.Globalization.CultureInfo.InvariantCulture), svg.Attribute("height")!.Value);
        Assert.Contains(svg.Descendants(), element => element.Name.LocalName is "path" or "rect" or "polyline");
    }

    private static Suspension CreateSuspensionWithVelocity(double[] velocity)
    {
        var compressionCount = velocity.TakeWhile(value => value > 0).Count();
        return new Suspension
        {
            Present = true,
            MaxTravel = 100,
            Travel = [.. Enumerable.Range(0, velocity.Length).Select(index => (double)index)],
            Velocity = [.. velocity],
            TravelBins = [0, 10, 20, 30, 40],
            VelocityBins = [-600, 0, 600],
            Strokes = new Strokes
            {
                Compressions = [new Stroke { Start = 0, End = compressionCount - 1, Stat = new StrokeStat { Count = compressionCount } }],
                Rebounds = [new Stroke { Start = compressionCount, End = velocity.Length - 1, Stat = new StrokeStat { Count = velocity.Length - compressionCount } }]
            }
        };
    }
}
