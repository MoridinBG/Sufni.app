using Sufni.App.Bikes.Services;
using Sufni.App.Setups.Models.SensorConfigurations;
namespace Sufni.App.Tests.Bikes.Services;

public class TelemetryBikeDataTests
{
    [Fact]
    public void Create_GivesTheLowestCountThatReachesMaxTravel()
    {
        var rear = new RearTravelCalibration(60, count => count % 4096 * 0.1, MeasurementWraps: true);

        var bikeData = TelemetryBikeData.Create(null, rear);

        Assert.Equal((ushort)600, bikeData.RearMaxTravelCount);
        Assert.Null(bikeData.FrontMaxTravelCount);
    }
}
