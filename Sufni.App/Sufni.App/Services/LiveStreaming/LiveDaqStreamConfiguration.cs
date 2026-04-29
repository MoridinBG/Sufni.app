namespace Sufni.App.Services.LiveStreaming;

public sealed record LiveDaqStreamConfiguration(
    LiveSensorInstanceMask RequestedSensorMask,
    uint TravelHz,
    uint ImuHz,
    uint GpsFixHz)
{
    public static readonly LiveDaqStreamConfiguration Default = new(
        LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu,
        TravelHz: 200,
        ImuHz: 200,
        GpsFixHz: 0);

    public static LiveDaqStreamConfiguration FromRequestedRates(uint travelHz, uint imuHz, uint gpsFixHz) => new(
        RequestedSensorMask: CreateSensorMask(travelHz, imuHz, gpsFixHz),
        TravelHz: travelHz,
        ImuHz: imuHz,
        GpsFixHz: gpsFixHz);

    public LiveStartRequest ToStartRequest() => new(
        RequestedSensorMask & CreateSensorMask(TravelHz, ImuHz, GpsFixHz),
        TravelHz,
        ImuHz,
        GpsFixHz);

    private static LiveSensorInstanceMask CreateSensorMask(uint travelHz, uint imuHz, uint gpsFixHz)
    {
        var sensorMask = LiveSensorInstanceMask.None;

        if (travelHz > 0)
        {
            sensorMask |= LiveSensorInstanceMask.Travel;
        }

        if (imuHz > 0)
        {
            sensorMask |= LiveSensorInstanceMask.Imu;
        }

        if (gpsFixHz > 0)
        {
            sensorMask |= LiveSensorInstanceMask.Gps;
        }

        return sensorMask;
    }
}
