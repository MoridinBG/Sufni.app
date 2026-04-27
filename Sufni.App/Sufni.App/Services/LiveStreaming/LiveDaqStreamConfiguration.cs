namespace Sufni.App.Services.LiveStreaming;

public sealed record LiveDaqStreamConfiguration(
    LiveSensorMask SensorMask,
    uint TravelHz,
    uint ImuHz,
    uint GpsFixHz)
{
    public static readonly LiveDaqStreamConfiguration Default = new(
        LiveSensorMask.Travel | LiveSensorMask.Imu,
        TravelHz: 200,
        ImuHz: 200,
        GpsFixHz: 0);

    public static LiveDaqStreamConfiguration FromRequestedRates(uint travelHz, uint imuHz, uint gpsFixHz) => new(
        SensorMask: CreateSensorMask(travelHz, imuHz, gpsFixHz),
        TravelHz: travelHz,
        ImuHz: imuHz,
        GpsFixHz: gpsFixHz);

    public LiveStartRequest ToStartRequest() => new(
        SensorMask & CreateSensorMask(TravelHz, ImuHz, GpsFixHz),
        TravelHz,
        ImuHz,
        GpsFixHz);

    private static LiveSensorMask CreateSensorMask(uint travelHz, uint imuHz, uint gpsFixHz)
    {
        var sensorMask = LiveSensorMask.None;

        if (travelHz > 0)
        {
            sensorMask |= LiveSensorMask.Travel;
        }

        if (imuHz > 0)
        {
            sensorMask |= LiveSensorMask.Imu;
        }

        if (gpsFixHz > 0)
        {
            sensorMask |= LiveSensorMask.Gps;
        }

        return sensorMask;
    }
}
