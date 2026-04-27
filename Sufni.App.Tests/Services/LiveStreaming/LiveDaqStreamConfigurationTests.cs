using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveDaqStreamConfigurationTests
{
    [Fact]
    public void Default_PrefillsTravelAndImu_AndDisablesGps()
    {
        var configuration = LiveDaqStreamConfiguration.Default;

        Assert.Equal(LiveSensorMask.Travel | LiveSensorMask.Imu, configuration.SensorMask);
        Assert.Equal((uint)200, configuration.TravelHz);
        Assert.Equal((uint)200, configuration.ImuHz);
        Assert.Equal((uint)0, configuration.GpsFixHz);
    }

    [Fact]
    public void ToStartRequest_OmitsStreamsWithZeroRates()
    {
        var configuration = new LiveDaqStreamConfiguration(
            SensorMask: LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps,
            TravelHz: 0,
            ImuHz: 200,
            GpsFixHz: 0);

        var request = configuration.ToStartRequest();

        Assert.Equal(LiveSensorMask.Imu, request.SensorMask);
        Assert.Equal((uint)0, request.TravelHz);
        Assert.Equal((uint)200, request.ImuHz);
        Assert.Equal((uint)0, request.GpsFixHz);
    }
}
