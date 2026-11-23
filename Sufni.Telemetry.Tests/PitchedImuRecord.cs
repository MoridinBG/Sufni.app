namespace Sufni.Telemetry.Tests;

public static class PitchedImuRecord
{
    /// <summary>A frame IMU record whose accelerometer reads a pitch angle and whose gyro reads a pitch rate (nose-up positive).</summary>
    public static ImuRecord At(double pitchDegrees, double accelLsbPerG, double pitchRateDps = 0, double gyroLsbPerDps = 0)
    {
        var pitch = pitchDegrees * Math.PI / 180;
        return new ImuRecord((short)Math.Round(accelLsbPerG * Math.Sin(pitch)), 0,
            (short)Math.Round(accelLsbPerG * Math.Cos(pitch)), 0, (short)Math.Round(-pitchRateDps * gyroLsbPerDps), 0);
    }
}
