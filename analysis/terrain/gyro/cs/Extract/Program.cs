// Read-only extraction of per-ride frame IMU, GPS motion, suspension travel and attitude estimates.
// Inputs come from gyro/export.py (BLOB + adapter-mapped fixes). Uses a scratchpad copy of Sufni.Telemetry.
using System.Numerics;
using System.Text.Json;
using Sufni.Telemetry;

var raw = args[0];
var outRoot = args[1];
var taus = new[] { 1.0, 2.0, 4.0, 8.0, 16.0 };

foreach (var blobPath in Directory.GetFiles(raw, "r*.blob").Order())
{
    var name = Path.GetFileNameWithoutExtension(blobPath);
    var dir = Directory.CreateDirectory(Path.Combine(outRoot, name)).FullName;
    var telemetry = TelemetryCodec.Decode(File.ReadAllBytes(blobPath));
    var fixRaw = File.ReadAllBytes(Path.ChangeExtension(blobPath, ".fix"));
    var f = new double[fixRaw.Length / 8];
    Buffer.BlockCopy(fixRaw, 0, f, 0, fixRaw.Length);
    // The receiver speed of a fix, matched by the rule of TrackSampleAdapter.FromOriginalFixes.
    var receiverSpeeds = new Dictionary<long, double>();
    foreach (var record in telemetry.GpsData ?? [])
        if (float.IsFinite(record.Speed) && record.Speed >= 0)
            receiverSpeeds.TryAdd(new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds(), record.Speed);
    var fixes = new List<TrackSample>();
    for (var i = 0; i < f.Length / 5; i++)
        fixes.Add(new TrackSample(f[5 * i], f[5 * i + 1], f[5 * i + 2], double.IsNaN(f[5 * i + 3]) ? null : f[5 * i + 3],
            receiverSpeeds.TryGetValue((long)Math.Round(f[5 * i + 4] * 1000), out var speed) ? speed : null));

    // 1. Full recomputation with the repository calculator (validation of inputs against stored profiles).
    var result = TerrainProfileCalculator.Calculate(telemetry, fixes);
    var gradeState = -1;
    using (var w = new StreamWriter(Path.Combine(dir, "windows.csv")))
    {
        w.WriteLine("start,end,speed,moving,grade,band");
        if (result is TerrainProfileResult.Computed computed)
        {
            gradeState = (int)computed.Profile.GradeState;
            foreach (var win in computed.Profile.Windows)
                w.WriteLine(FormattableString.Invariant(
                    $"{win.StartSeconds:R},{win.EndSeconds:R},{win.MeanSpeed:R},{(win.Moving ? 1 : 0)},{(win.Grade is { } g ? g.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "")},{(win.Band is { } b ? ((int)b).ToString() : "")}"));
        }
    }

    using (var w = new StreamWriter(Path.Combine(dir, "pieces.csv")))
    {
        w.WriteLine("start,end,grade,band");
        if (result is TerrainProfileResult.Computed computed)
            foreach (var piece in computed.Profile.GradePieces)
                w.WriteLine(FormattableString.Invariant(
                    $"{piece.StartSeconds:R},{piece.EndSeconds:R},{(piece.Grade is { } g ? g.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "")},{(piece.Band is { } b ? ((int)b).ToString() : "")}"));
    }

    // 2. Motion grid.
    var motion = TrackMotion.FromFixes(fixes, telemetry.Metadata.Duration);
    using (var g = new BinaryWriter(File.Create(Path.Combine(dir, "grid.bin"))))
    {
        for (var s = 0; s < motion.Spans.Length; s++)
            foreach (var p in motion.Spans[s])
            {
                g.Write((double)s); g.Write(p.Seconds); g.Write(p.Distance); g.Write(p.Elevation ?? double.NaN);
                g.Write(p.Speed); g.Write(p.Acceleration);
            }
    }

    // 3. Frame IMU samples, motion at each sample, travel at each sample, attitude estimates.
    var imu = telemetry.ImuData;
    var frame = imu is { SampleRate: >= 200 } && imu.ActiveLocations.Contains((byte)ImuLocation.Frame)
        ? imu.Meta.FirstOrDefault(m => m.LocationId == (byte)ImuLocation.Frame && m.AccelLsbPerG > 0) : null;
    var segments = new List<object>();
    if (imu is not null && frame is not null)
    {
        using var ib = new BinaryWriter(File.Create(Path.Combine(dir, "imu.bin")));
        using var mb = new BinaryWriter(File.Create(Path.Combine(dir, "mot.bin")));
        using var tb = new BinaryWriter(File.Create(Path.Combine(dir, "trav.bin")));
        using var ab = new BinaryWriter(File.Create(Path.Combine(dir, "att.bin")));
        long offset = 0;
        foreach (var seg in imu.SampleSegments)
        {
            if (seg.LocationId != frame.LocationId || seg.Count == 0) continue;
            var origin = seg.FirstMonotonicDeltaUs / 1e6;
            var rate = (double)imu.SampleRate;
            var repo = FrameAttitudeEstimator.Recorded(seg, rate, frame, motion);
            var variants = taus.Select(t => Attitude.Recorded(seg, rate, frame, motion, t, true)).ToList();
            variants.Add(Attitude.Recorded(seg, rate, frame, null, 4.0, false));
            for (var i = 0; i < seg.Count; i++)
            {
                var r = seg[i];
                ib.Write(r.Ax); ib.Write(r.Ay); ib.Write(r.Az); ib.Write(r.Gx); ib.Write(r.Gy); ib.Write(r.Gz);
                var t = origin + (double)i / imu.SampleRate;
                var at = motion.At(t);
                mb.Write(at is null ? 0f : 1f); mb.Write((float)(at?.Speed ?? 0)); mb.Write((float)(at?.Acceleration ?? 0));
                tb.Write(TravelAt(telemetry.Front, telemetry.Metadata.SampleRate, t));
                tb.Write(TravelAt(telemetry.Rear, telemetry.Metadata.SampleRate, t));
                ab.Write(repo[i].Up.X); ab.Write(repo[i].Up.Y); ab.Write(repo[i].Up.Z);
                foreach (var v in variants) ab.Write(v[i].X);
            }
            segments.Add(new { origin, count = seg.Count, offset });
            offset += seg.Count;
        }
    }
    var meta = new
    {
        gradeState,
        imuRate = imu?.SampleRate,
        accelLsb = frame?.AccelLsbPerG,
        gyroLsb = frame?.GyroLsbPerDps,
        suspensionRate = telemetry.Metadata.SampleRate,
        duration = telemetry.Metadata.Duration,
        front = telemetry.Front is { Present: true }, rear = telemetry.Rear is { Present: true },
        frontMax = telemetry.Front?.MaxTravel, rearMax = telemetry.Rear?.MaxTravel,
        taus, attColumns = "repoUx,repoUy,repoUz," + string.Join(",", taus.Select(t => $"tau{t}Ux")) + ",tau4noTrackUx",
        segments
    };
    File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta));
    Console.WriteLine($"{name}: gradeState={gradeState} segments={segments.Count}");
}

static float TravelAt(Suspension? side, int rate, double t)
{
    if (side is not { Present: true } || side.Travel.IsDefaultOrEmpty || rate <= 0) return float.NaN;
    var segs = side.Segments.IsDefaultOrEmpty
        ? [new ProcessedSuspensionSegment { SampleCount = side.Travel.Length }] : side.Segments;
    foreach (var s in segs)
    {
        var k = (int)Math.Floor((t - s.StartSeconds) * rate);
        if (k >= 0 && k < s.SampleCount) return (float)side.Travel[s.FirstDenseIndex + k];
    }
    return float.NaN;
}

// Copy of FrameAttitudeEstimator with the correction time constant and the motion compensation as parameters.
static class Attitude
{
    private const double Gravity = 9.80665;

    public static Quaternion Advance(Quaternion? previous, ImuRecord sample, double dt, double accelLsbPerG,
        double gyroLsbPerDps, TrackMotionSample? motion, double tau, bool compensate)
    {
        var omega = new Vector3(sample.Gx, sample.Gy, sample.Gz) * (float)(Math.PI / (180 * gyroLsbPerDps));
        var accel = new Vector3(sample.Ax, sample.Ay, sample.Az) / (float)accelLsbPerG;
        if (compensate && motion is { } track)
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
        var correction = Vector3.Cross(measured, FrameAttitudeEstimator.Read(orientation).Up);
        var rotation = omega * (float)dt + correction * (float)(Math.Abs(dt) / tau);
        var angle = rotation.Length();
        return angle == 0 ? orientation : Quaternion.Normalize(orientation *
            Quaternion.CreateFromAxisAngle(rotation / angle, angle));
    }

    public static Vector3[] Recorded(ImuSampleSegment segment, double rate, ImuMetaEntry meta, TrackMotion? track,
        double tau, bool compensate)
    {
        var up = new Vector3[segment.Count];
        var dt = 1 / rate;
        var origin = segment.FirstMonotonicDeltaUs / 1e6;
        Quaternion? rotation = null;
        for (var i = 0; i < segment.Count; i++)
        {
            rotation = Advance(rotation, segment[i], dt, meta.AccelLsbPerG, meta.GyroLsbPerDps, track?.At(origin + i / rate), tau, compensate);
            up[i] = FrameAttitudeEstimator.Read(rotation.Value).Up;
        }
        rotation = null;
        for (var i = segment.Count - 1; i >= 0; i--)
        {
            rotation = Advance(rotation, segment[i], -dt, meta.AccelLsbPerG, meta.GyroLsbPerDps, track?.At(origin + i / rate), tau, compensate);
            var mean = up[i] + FrameAttitudeEstimator.Read(rotation.Value).Up;
            if (mean.LengthSquared() > 0) up[i] = Vector3.Normalize(mean);
        }
        return up;
    }
}
