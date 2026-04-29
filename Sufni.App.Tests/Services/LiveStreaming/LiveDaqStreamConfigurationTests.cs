using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveDaqStreamConfigurationTests
{
    [Fact]
    public void Default_PrefillsTravelAndImu_AndDisablesGps()
    {
        var configuration = LiveDaqStreamConfiguration.Default;

        Assert.Equal(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu, configuration.RequestedSensorMask);
        Assert.Equal((uint)200, configuration.TravelHz);
        Assert.Equal((uint)200, configuration.ImuHz);
        Assert.Equal((uint)0, configuration.GpsFixHz);
    }

    [Fact]
    public void ToStartRequest_OmitsStreamsWithZeroRates()
    {
        var configuration = new LiveDaqStreamConfiguration(
            RequestedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps,
            TravelHz: 0,
            ImuHz: 200,
            GpsFixHz: 0);

        var request = configuration.ToStartRequest();

        Assert.Equal(LiveSensorInstanceMask.Imu, request.RequestedSensorMask);
        Assert.Equal((uint)0, request.TravelHz);
        Assert.Equal((uint)200, request.ImuHz);
        Assert.Equal((uint)0, request.GpsFixHz);
    }

    [Fact]
    public void FromRequestedRates_DerivesIndividualMaskFromNonzeroRates()
    {
        var travelOnly = LiveDaqStreamConfiguration.FromRequestedRates(100, 0, 0);
        var imuOnly = LiveDaqStreamConfiguration.FromRequestedRates(0, 200, 0);
        var gpsOnly = LiveDaqStreamConfiguration.FromRequestedRates(0, 0, 10);

        Assert.Equal(LiveSensorInstanceMask.Travel, travelOnly.RequestedSensorMask);
        Assert.Equal(LiveSensorInstanceMask.Imu, imuOnly.RequestedSensorMask);
        Assert.Equal(LiveSensorInstanceMask.Gps, gpsOnly.RequestedSensorMask);
    }
}
