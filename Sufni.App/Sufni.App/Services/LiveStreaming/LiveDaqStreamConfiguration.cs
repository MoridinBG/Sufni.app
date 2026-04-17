namespace Sufni.App.Services.LiveStreaming;

public sealed record LiveDaqStreamConfiguration(
    LiveSensorMask SensorMask,
    uint TravelHz,
    uint ImuHz,
    uint GpsFixHz)
{
    public static readonly LiveDaqStreamConfiguration Default = new(
        LiveSensorMask.Travel | LiveSensorMask.Imu,
        TravelHz: 0,
        ImuHz: 0,
        GpsFixHz: 0);

    public LiveStartRequest ToStartRequest() => new(
        SensorMask,
        TravelHz,
        ImuHz,
        GpsFixHz);
}