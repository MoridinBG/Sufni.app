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
    private const double RecordedCorrectionSeconds = 10;
    private const double StartSeconds = 2;

    public static Quaternion Advance(Quaternion? previous, ImuRecord sample, double dt,
        double accelLsbPerG, double gyroLsbPerDps)
    {
        var omega = new Vector3(sample.Gx, sample.Gy, sample.Gz) * (float)(Math.PI / (180 * gyroLsbPerDps));
        var accel = new Vector3(sample.Ax, sample.Ay, sample.Az) / (float)accelLsbPerG;
        var measured = accel == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(accel);
        if (previous is not { } orientation)
        {
            if (measured == Vector3.Zero) return Quaternion.Identity;
            var pitch = MathF.Asin(Math.Clamp(measured.X, -1, 1));
            var roll = MathF.Atan2(measured.Y, measured.Z);
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pitch) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll);
        }

        var correction = Vector3.Cross(measured, Read(orientation).Up);
        var rotation = omega * (float)dt + correction * (float)(dt / CorrectionSeconds);
        var angle = rotation.Length();
        return angle == 0 ? orientation : Quaternion.Normalize(orientation *
            Quaternion.CreateFromAxisAngle(rotation / angle, angle));
    }

    public static FrameAttitude Read(Quaternion orientation) =>
        new(Vector3.Transform(Vector3.UnitZ, Quaternion.Conjugate(orientation)));

    /// <summary>
    /// The up direction of each sample from a forward and a backward pass over the whole segment. The gyroscope turns
    /// the up direction and the accelerometer pulls it slowly towards the measured gravity. With a track, the
    /// accelerations of speed change and of turning are removed from the measured gravity first.
    /// </summary>
    public static FrameAttitude[] Recorded(ImuSampleSegment segment, double rate, ImuMetaEntry meta,
        TrackMotion? track = null)
    {
        var count = segment.Count;
        var rotation = new Vector[count];
        var gravity = new Vector[count];
        var origin = segment.FirstMonotonicDeltaUs / 1e6;
        var radiansPerCount = Math.PI / (180 * meta.GyroLsbPerDps);
        for (var i = 0; i < count; i++)
        {
            var sample = segment[i];
            rotation[i] = new Vector(sample.Gx, sample.Gy, sample.Gz) * radiansPerCount;
            // Not normalised: under vibration the mean of the raw vector keeps the direction of gravity.
            gravity[i] = new Vector(sample.Ax, sample.Ay, sample.Az) * (1d / meta.AccelLsbPerG);
            if (track?.At(origin + i / rate) is { } motion)
                gravity[i] -= new Vector(motion.Acceleration, motion.ForwardSpeed * rotation[i].Z,
                    -motion.ForwardSpeed * rotation[i].Y) * (1 / Gravity);
        }

        var forward = new Vector[count];
        var dt = 1 / rate;
        var startCount = Math.Min(count, (int)Math.Round(StartSeconds * rate));
        var up = Start(gravity, 0, startCount);
        for (var i = 0; i < count; i++)
            forward[i] = up = Step(up, rotation[i], gravity[i], dt);

        var attitudes = new FrameAttitude[count];
        up = Start(gravity, count - startCount, count);
        for (var i = count - 1; i >= 0; i--)
        {
            up = Step(up, -rotation[i], gravity[i], dt);
            var sum = forward[i] + up;
            attitudes[i] = new FrameAttitude((sum.Length > 0 ? sum.Normalized() : forward[i]).ToVector3());
        }
        return attitudes;
    }

    private static Vector Start(Vector[] gravity, int from, int to)
    {
        var sum = new Vector(0, 0, 0);
        for (var i = from; i < to; i++) sum += gravity[i];
        return sum.Length > 0 ? sum.Normalized() : new Vector(0, 0, 1);
    }

    private static Vector Step(Vector up, Vector rotation, Vector gravity, double dt) =>
        (up + (up.Cross(rotation) + (gravity - up) * (1 / RecordedCorrectionSeconds)) * dt).Normalized();

    private readonly record struct Vector(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vector Normalized() => this * (1 / Length);
        public Vector Cross(Vector o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
        public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector operator -(Vector a) => new(-a.X, -a.Y, -a.Z);
        public static Vector operator *(Vector a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    }
}
