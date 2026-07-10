using System;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveDaqStreamConfiguration(
    LiveSensorInstanceMask RequestedSensorMask,
    uint TravelRateMhz,
    uint ImuRateMhz,
    uint GpsRateMhz,
    LiveStreamMask RequestedStreamMask = LiveStreamMask.None,
    uint TemperatureRateMhz = 0,
    uint? TravelBatchDurationMs = null,
    uint? ImuBatchDurationMs = null,
    bool RequestGpsDiagnostics = false,
    bool Priority = false,
    bool NoGpsHeaderWait = false)
{
    public static readonly LiveDaqStreamConfiguration Default = new(
        LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu,
        TravelRateMhz: LiveProtocolHelpers.HertzToMillihertz(200),
        ImuRateMhz: LiveProtocolHelpers.HertzToMillihertz(200),
        GpsRateMhz: 0,
        RequestedStreamMask: LiveStreamMask.Travel | LiveStreamMask.Imu);

    public uint TravelHz => LiveProtocolHelpers.MillihertzToWholeHertz(TravelRateMhz);

    public uint ImuHz => LiveProtocolHelpers.MillihertzToWholeHertz(ImuRateMhz);

    public uint GpsFixHz => LiveProtocolHelpers.MillihertzToWholeHertz(GpsRateMhz);

    public uint TemperatureHz => LiveProtocolHelpers.MillihertzToWholeHertz(TemperatureRateMhz);

    public static LiveDaqStreamConfiguration FromRequestedRates(uint travelHz, uint imuHz, uint gpsFixHz)
    {
        var travelRateMhz = LiveProtocolHelpers.HertzToMillihertz(travelHz);
        var imuRateMhz = LiveProtocolHelpers.HertzToMillihertz(imuHz);
        var gpsRateMhz = LiveProtocolHelpers.HertzToMillihertz(gpsFixHz);

        return new LiveDaqStreamConfiguration(
            RequestedSensorMask: CreateSensorMask(travelRateMhz, imuRateMhz, gpsRateMhz),
            TravelRateMhz: travelRateMhz,
            ImuRateMhz: imuRateMhz,
            GpsRateMhz: gpsRateMhz,
            RequestedStreamMask: CreateStreamMask(travelRateMhz, imuRateMhz, gpsRateMhz));
    }

    public LiveStartRequest ToStartRequest()
    {
        var requestedStreams = RequestedStreamMask == LiveStreamMask.None
            ? CreateStreamMask(TravelRateMhz, ImuRateMhz, GpsRateMhz)
            : RequestedStreamMask;
        if (NoGpsHeaderWait && (requestedStreams & LiveStreamMask.Gps) == 0)
        {
            throw new InvalidOperationException("NO_GPS_HEADER_WAIT requires an explicit GPS stream request.");
        }
        const LiveStreamMask telemetryStreams =
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps;
        if ((requestedStreams & LiveStreamMask.Marker) != 0 &&
            (requestedStreams & telemetryStreams) == 0)
        {
            throw new InvalidOperationException(
                "A LIVE v3 marker stream requires at least one telemetry stream.");
        }

        var requestedSensors = RequestedSensorMask;
        if (RequestedStreamMask == LiveStreamMask.None)
        {
            requestedSensors &= CreateSensorMask(TravelRateMhz, ImuRateMhz, GpsRateMhz);
        }

        if ((requestedStreams & LiveStreamMask.Gps) != 0)
        {
            requestedSensors |= LiveSensorInstanceMask.Gps;
        }
        if ((requestedStreams & LiveStreamMask.Battery) != 0)
        {
            requestedSensors |= LiveSensorInstanceMask.Battery;
        }

        return new LiveStartRequest(
            requestedSensors,
            TravelRateMhz,
            ImuRateMhz,
            GpsRateMhz,
            requestedStreams,
            TemperatureRateMhz,
            TravelBatchDurationMs,
            ImuBatchDurationMs,
            RequestGpsDiagnostics,
            Priority,
            NoGpsHeaderWait);
    }

    private static LiveSensorInstanceMask CreateSensorMask(uint travelRateMhz, uint imuRateMhz, uint gpsRateMhz)
    {
        var sensorMask = LiveSensorInstanceMask.None;

        if (travelRateMhz > 0)
        {
            sensorMask |= LiveSensorInstanceMask.Travel;
        }

        if (imuRateMhz > 0)
        {
            sensorMask |= LiveSensorInstanceMask.Imu;
        }

        if (gpsRateMhz > 0)
        {
            sensorMask |= LiveSensorInstanceMask.Gps;
        }

        return sensorMask;
    }

    private static LiveStreamMask CreateStreamMask(uint travelRateMhz, uint imuRateMhz, uint gpsRateMhz)
    {
        var streamMask = LiveStreamMask.None;
        if (travelRateMhz > 0) streamMask |= LiveStreamMask.Travel;
        if (imuRateMhz > 0) streamMask |= LiveStreamMask.Imu;
        if (gpsRateMhz > 0) streamMask |= LiveStreamMask.Gps;
        return streamMask;
    }
}
