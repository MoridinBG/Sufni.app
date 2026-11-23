using System.Numerics;

namespace Sufni.Telemetry;

public readonly record struct FrameAttitude(Vector3 Up)
{
    public double PitchDegrees => Math.Asin(Math.Clamp(Up.X, -1, 1)) * 180 / Math.PI;
    public double RollDegrees => Math.Atan2(Up.Y, Up.Z) * 180 / Math.PI;
}

/// <summary>Bike-to-world orientation for one continuous frame IMU segment.</summary>
public static class FrameAttitudeEstimator
{
    private const double Gravity = 9.80665;
    private const double CorrectionSeconds = 4;

    public static Quaternion Advance(Quaternion? previous, ImuRecord sample, double dt,
        double accelLsbPerG, double gyroLsbPerDps, TrackMotionSample? motion = null)
    {
        var omega = new Vector3(sample.Gx, sample.Gy, sample.Gz) * (float)(Math.PI / (180 * gyroLsbPerDps));
        var accel = new Vector3(sample.Ax, sample.Ay, sample.Az) / (float)accelLsbPerG;
        if (motion is { } track)
            accel -= new Vector3((float)track.Acceleration, (float)track.Speed * omega.Z, -(float)track.Speed * omega.Y)
                / (float)Gravity;
        var measured = accel == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(accel);
        if (previous is not { } orientation)
        {
            if (measured == Vector3.Zero) return Quaternion.Identity;
            var pitch = MathF.Asin(Math.Clamp(measured.X, -1, 1));
            var roll = MathF.Atan2(measured.Y, measured.Z);
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pitch) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll);
        }

        var correction = Vector3.Cross(measured, Read(orientation).Up);
        // In reverse time the gyro rotation reverses; the correction still attracts towards measured up.
        var rotation = omega * (float)dt + correction * (float)(Math.Abs(dt) / CorrectionSeconds);
        var angle = rotation.Length();
        return angle == 0 ? orientation : Quaternion.Normalize(orientation *
            Quaternion.CreateFromAxisAngle(rotation / angle, angle));
    }

    public static FrameAttitude Read(Quaternion orientation) =>
        new(Vector3.Transform(Vector3.UnitZ, Quaternion.Conjugate(orientation)));

    public static FrameAttitude[] Recorded(ImuSampleSegment segment, double rate, ImuMetaEntry meta,
        TrackMotion? track = null)
    {
        var attitudes = new FrameAttitude[segment.Count];
        var dt = 1 / rate;
        var origin = segment.FirstMonotonicDeltaUs / 1e6;
        Quaternion? rotation = null;
        for (var i = 0; i < segment.Count; i++)
        {
            rotation = Advance(rotation, segment[i], dt, meta.AccelLsbPerG, meta.GyroLsbPerDps, track?.At(origin + i / rate));
            attitudes[i] = Read(rotation.Value);
        }

        rotation = null;
        for (var i = segment.Count - 1; i >= 0; i--)
        {
            rotation = Advance(rotation, segment[i], -dt, meta.AccelLsbPerG, meta.GyroLsbPerDps, track?.At(origin + i / rate));
            var mean = attitudes[i].Up + Read(rotation.Value).Up;
            if (mean.LengthSquared() > 0) attitudes[i] = new FrameAttitude(Vector3.Normalize(mean));
        }
        return attitudes;
    }
}
