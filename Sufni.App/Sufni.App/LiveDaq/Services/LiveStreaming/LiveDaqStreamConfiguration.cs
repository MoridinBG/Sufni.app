namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveDaqStreamConfiguration(
    LiveSensorInstanceMask RequestedSensorMask,
    uint TravelRateMhz,
    uint ImuRateMhz,
    uint GpsRateMhz)
{
    public static readonly LiveDaqStreamConfiguration Default = new(
        LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu,
        TravelRateMhz: LiveProtocolHelpers.HertzToMillihertz(200),
        ImuRateMhz: LiveProtocolHelpers.HertzToMillihertz(200),
        GpsRateMhz: 0);

    public uint TravelHz => LiveProtocolHelpers.MillihertzToWholeHertz(TravelRateMhz);

    public uint ImuHz => LiveProtocolHelpers.MillihertzToWholeHertz(ImuRateMhz);

    public uint GpsFixHz => LiveProtocolHelpers.MillihertzToWholeHertz(GpsRateMhz);

    public static LiveDaqStreamConfiguration FromRequestedRates(uint travelHz, uint imuHz, uint gpsFixHz)
    {
        var travelRateMhz = LiveProtocolHelpers.HertzToMillihertz(travelHz);
        var imuRateMhz = LiveProtocolHelpers.HertzToMillihertz(imuHz);
        var gpsRateMhz = LiveProtocolHelpers.HertzToMillihertz(gpsFixHz);

        return new LiveDaqStreamConfiguration(
            RequestedSensorMask: CreateSensorMask(travelRateMhz, imuRateMhz, gpsRateMhz),
            TravelRateMhz: travelRateMhz,
            ImuRateMhz: imuRateMhz,
            GpsRateMhz: gpsRateMhz);
    }

    public LiveStartRequest ToStartRequest() => new(
        RequestedSensorMask & CreateSensorMask(TravelRateMhz, ImuRateMhz, GpsRateMhz),
        TravelRateMhz,
        ImuRateMhz,
        GpsRateMhz);

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
}
