
using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqStreamConfigurationTests
{
    [Fact]
    public void ToStartRequest_OmitsStreamsWithZeroRates()
    {
        var configuration = new LiveDaqStreamConfiguration(
            RequestedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps,
            TravelRateMhz: 0,
            ImuRateMhz: 200_000,
            GpsRateMhz: 0);

        var request = configuration.ToStartRequest();

        Assert.Equal(LiveSensorInstanceMask.Imu, request.RequestedSensorMask);
        Assert.Equal((uint)0, request.TravelRateMhz);
        Assert.Equal((uint)200_000, request.ImuRateMhz);
        Assert.Equal((uint)0, request.GpsRateMhz);
    }

}
