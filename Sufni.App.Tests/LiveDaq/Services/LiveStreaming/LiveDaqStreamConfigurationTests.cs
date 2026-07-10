
using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqStreamConfigurationTests
{
    [Fact]
    public void Default_PrefillsTravelAndImu_AndDisablesGps()
    {
        var configuration = LiveDaqStreamConfiguration.Default;

        Assert.Equal(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu, configuration.RequestedSensorMask);
        Assert.Equal((uint)200_000, configuration.TravelRateMhz);
        Assert.Equal((uint)200_000, configuration.ImuRateMhz);
        Assert.Equal((uint)0, configuration.GpsRateMhz);
        Assert.Equal((uint)200, configuration.TravelHz);
        Assert.Equal((uint)200, configuration.ImuHz);
        Assert.Equal((uint)0, configuration.GpsFixHz);
    }

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

    [Fact]
    public void FromRequestedRates_DerivesIndividualMaskFromNonzeroRates()
    {
        var travelOnly = LiveDaqStreamConfiguration.FromRequestedRates(100, 0, 0);
        var imuOnly = LiveDaqStreamConfiguration.FromRequestedRates(0, 200, 0);
        var gpsOnly = LiveDaqStreamConfiguration.FromRequestedRates(0, 0, 10);

        Assert.Equal(LiveSensorInstanceMask.Travel, travelOnly.RequestedSensorMask);
        Assert.Equal(LiveSensorInstanceMask.Imu, imuOnly.RequestedSensorMask);
        Assert.Equal(LiveSensorInstanceMask.Gps, gpsOnly.RequestedSensorMask);
        Assert.Equal((uint)100_000, travelOnly.TravelRateMhz);
        Assert.Equal((uint)200_000, imuOnly.ImuRateMhz);
        Assert.Equal((uint)10_000, gpsOnly.GpsRateMhz);
        Assert.Equal((uint)100, travelOnly.TravelHz);
        Assert.Equal((uint)200, imuOnly.ImuHz);
        Assert.Equal((uint)10, gpsOnly.GpsFixHz);
    }

    [Fact]
    public void ToStartRequest_PreservesExplicitV3StreamAndOverrideChoices()
    {
        var configuration = new LiveDaqStreamConfiguration(
            RequestedSensorMask: LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.Gps,
            TravelRateMhz: 0,
            ImuRateMhz: 0,
            GpsRateMhz: 5_000,
            RequestedStreamMask: LiveStreamMask.Temperature |
                                 LiveStreamMask.Gps |
                                 LiveStreamMask.Battery |
                                 LiveStreamMask.Marker,
            TemperatureRateMhz: 30,
            RequestGpsDiagnostics: true,
            Priority: true,
            NoGpsHeaderWait: true);

        var request = configuration.ToStartRequest();

        Assert.Equal(configuration.RequestedStreamMask, request.RequestedStreamMask);
        Assert.Equal(30u, request.TemperatureRateMhz);
        Assert.True(request.RequestGpsDiagnostics);
        Assert.True(request.Priority);
        Assert.True(request.NoGpsHeaderWait);
        Assert.NotEqual(
            LiveSensorInstanceMask.None,
            request.RequestedSensorMask & LiveSensorInstanceMask.Battery);
    }

    [Fact]
    public void ToStartRequest_RejectsNoGpsHeaderWaitWithoutGpsSelection()
    {
        var configuration = LiveDaqStreamConfiguration.Default with
        {
            NoGpsHeaderWait = true,
        };

        Assert.Throws<InvalidOperationException>(() => configuration.ToStartRequest());
    }

    [Fact]
    public void ToStartRequest_RejectsMarkerWithoutTelemetry()
    {
        var configuration = new LiveDaqStreamConfiguration(
            RequestedSensorMask: LiveSensorInstanceMask.None,
            TravelRateMhz: 0,
            ImuRateMhz: 0,
            GpsRateMhz: 0,
            RequestedStreamMask: LiveStreamMask.Marker);

        Assert.Throws<InvalidOperationException>(() => configuration.ToStartRequest());
    }
}
