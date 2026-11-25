using System.Collections.Immutable;

namespace Sufni.Telemetry.Tests;

public sealed record RideSection(double Length, double Speed = 7, double GradeDegrees = 0,
    double LowBandG = 0.4, double MiddleBandG = 0.6, double HighBandG = 0.7, double TurnRadius = 0,
    double CompressionGain = 1, double ReboundGain = 1, double PedalHz = 0, double BobMillimetres = 0);

public sealed record RideRecording(
    int SampleRate = 200, double FixRate = 1, bool IrregularFixes = false, double FirstFix = 0, double TrackShift = 0,
    TelemetryTimeRange? FixLoss = null, TelemetryTimeRange? RecordingGap = null, TelemetryTimeRange? Stop = null,
    TelemetryTimeRange? Braking = null, double BrakingAcceleration = 2.94, double MountPitch = 0, double Roll = 0,
    bool Frame = true, bool Fork = true, bool RearAxle = false, int AccelScale = 4096,
    int Seed = 17, double NoiseG = 0, int? ForkAccelScale = null, (TerrainSensor Sensor, TelemetryTimeRange Range)? Silence = null);

public sealed record SyntheticRide(TelemetryData Telemetry, ImmutableArray<TrackSample> Fixes)
{
    private const double Gravity = 9.80665;
    private const double Radians = Math.PI / 180;
    private const int GyroScale = 16;

    /// <summary>The fork vibration level relative to the frame level at the same speed.</summary>
    public const double ForkLevelFactor = 1.75;

    /// <summary>The rear axle vibration level relative to the frame level at the same speed.</summary>
    public const double RearAxleLevelFactor = 1.5;

    /// <summary>The frame level, in g at 7 m/s, at the centre of Medium on <see cref="Scale"/>.</summary>
    public const double MediumFrameG = 1.75;

    /// <summary>The scale on which synthetic descent levels take their class. It sets no climb class.</summary>
    public static readonly TerrainScaleKey Scale = new(TerrainLevels.Bin(MediumFrameG),
        TerrainLevels.Bin(ForkLevelFactor * MediumFrameG), TerrainLevels.Bin(RearAxleLevelFactor * MediumFrameG), null);

    /// <summary>Frame vertical acceleration amplitude in g at the pedal rhythm.</summary>
    public const double PedalFrameG = 0.1;

    public static SyntheticRide TurningRide(RideRecording recording) =>
        Build([new(112), new(70, TurnRadius: -26), new(91, TurnRadius: 18), new(49),
            new(140, TurnRadius: 33), new(77, TurnRadius: -19), new(91), new(63, TurnRadius: -35)], recording);

    public TerrainProfile Profile() =>
        Assert.IsType<TerrainProfileResult.Computed>(TerrainProfileCalculator.Calculate(Telemetry, Fixes)).Profile;

    public static SyntheticRide Build(IReadOnlyList<RideSection>? sections = null, RideRecording? recording = null)
    {
        sections ??= [new(1980, GradeDegrees: 3), new(858, GradeDegrees: 8, LowBandG: 0.9, MiddleBandG: 1, HighBandG: 1.2),
            new(462, GradeDegrees: 14, LowBandG: 1.4, MiddleBandG: 1.6, HighBandG: 1.8)];
        recording ??= new();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recording.SampleRate);
        if (!(recording.FixRate > 0) || recording.FirstFix < 0 || sections.Count == 0 ||
            sections.Any(s => !(s.Speed > 0 && s.Length > 0))) throw new ArgumentException("Invalid ride description.");

        var rate = recording.SampleRate;
        var random = new Random(recording.Seed);
        var fixClock = new Random(recording.Seed);
        var phases = Enumerable.Range(0, 3).Select(_ => 2 * Math.PI * random.NextDouble()).ToArray();
        var fixes = ImmutableArray.CreateBuilder<TrackSample>();
        var sensors = new List<Sensor>();
        if (recording.Frame) sensors.Add(new Sensor(ImuLocation.Frame, recording.AccelScale));
        if (recording.Fork) sensors.Add(new Sensor(ImuLocation.Fork, recording.ForkAccelScale ?? recording.AccelScale));
        if (recording.RearAxle) sensors.Add(new Sensor(ImuLocation.RearAxle, recording.AccelScale));
        var front = new Side(rate);
        var rear = new Side(rate);
        var nextFix = recording.FirstFix;
        double east = 0, north = 0, elevation = 1000, heading = 0, distance = 0, sectionStart = 0;
        var sectionIndex = 0;
        var lastSpeed = sections[0].Speed;
        var previousPitch = (recording.MountPitch - sections[0].GradeDegrees) * Radians;
        var roll = recording.Roll * Radians;
        var tick = 0;
        while (true)
        {
            var seconds = (double)tick / rate;
            var section = sections[sectionIndex];
            var speed = Speed(section.Speed, seconds, recording);
            var pitch = (recording.MountPitch - section.GradeDegrees) * Radians;
            var pitchRate = (pitch - previousPitch) * rate;
            var yawRate = section.TurnRadius == 0 ? 0 : speed / section.TurnRadius;
            var upX = Math.Sin(pitch);
            var upY = Math.Sin(roll) * Math.Cos(pitch);
            var upZ = Math.Cos(roll) * Math.Cos(pitch);
            var gy = upY * yawRate - Math.Cos(roll) * pitchRate;
            var gz = upZ * yawRate + Math.Sin(roll) * pitchRate;
            var ground = Math.Sqrt(2) * (
                section.LowBandG * Math.Sin(2 * Math.PI * 3 * seconds + phases[0]) +
                section.MiddleBandG * Math.Sin(2 * Math.PI * 12 * seconds + phases[1]) +
                section.HighBandG * Math.Sin(2 * Math.PI * 45 * seconds + phases[2]));
            if (!Inside(recording.RecordingGap, seconds))
            {
                bool Silent(TerrainSensor sensor) => recording.Silence is { } silence && silence.Sensor == sensor &&
                    Inside(silence.Range, seconds);
                foreach (var sensor in sensors)
                {
                    if (Silent(sensor.Identity))
                    {
                        sensor.Add(tick, rate, 0, 0, 0, 0, 0, 0);
                        continue;
                    }
                    var vibration = ground * sensor.LevelScale(speed) + recording.NoiseG * (2 * random.NextDouble() - 1);
                    var pedal = sensor.Identity == TerrainSensor.FrameImu
                        ? PedalFrameG * Math.Sin(2 * Math.PI * section.PedalHz * seconds) : 0;
                    sensor.Add(tick, rate, upX + (speed - lastSpeed) * rate / Gravity,
                        upY + speed * gz / Gravity + vibration, upZ - speed * gy / Gravity + pedal,
                        upX * yawRate / Radians, gy / Radians, gz / Radians);
                }
                var level = Math.Sqrt(section.LowBandG * section.LowBandG +
                    section.MiddleBandG * section.MiddleBandG + section.HighBandG * section.HighBandG);
                var response = ResponseAmplitude(level, speed);
                front.Add(tick, Silent(TerrainSensor.ForkTravel) ? 0 : response, section, 0);
                rear.Add(tick, Silent(TerrainSensor.ShockTravel) ? 0 : response * 0.8, section, Silent(TerrainSensor.ShockTravel) ? 0 : section.BobMillimetres);
            }

            var remaining = section.Length - (distance - sectionStart);
            var step = Math.Min(speed / rate, Math.Max(0, remaining));
            var angle = heading + yawRate / (2 * rate);
            var dx = step * Math.Cos(angle);
            var dy = step * Math.Sin(angle);
            var dz = -step * Math.Sin(section.GradeDegrees * Radians);
            var finished = remaining <= 1e-8 && sectionIndex == sections.Count - 1;
            var fixHorizon = finished ? seconds : (tick + 1d) / rate;
            while (nextFix <= fixHorizon)
            {
                var weight = Math.Clamp((nextFix - seconds) * rate, 0, 1);
                if (!Inside(recording.FixLoss, nextFix))
                    fixes.Add(new(nextFix + recording.TrackShift, east + weight * dx, north + weight * dy, elevation + weight * dz));
                nextFix += (recording.IrregularFixes ? 0.75 + 0.5 * fixClock.NextDouble() : 1) / recording.FixRate;
            }
            if (finished) break;
            distance += step;
            east += dx;
            north += dy;
            elevation += dz;
            heading += yawRate / rate;
            if (remaining - step <= 1e-8 && sectionIndex + 1 < sections.Count)
            {
                sectionStart = distance;
                sectionIndex++;
            }
            lastSpeed = speed;
            previousPitch = pitch;
            tick++;
        }

        var telemetry = new TelemetryData
        {
            Metadata = new Metadata { SourceName = "synthetic.sst", Version = 5, Timestamp = 1_700_000_000,
                Duration = (double)tick / rate, SampleRate = rate },
            ImuData = sensors.Count == 0 ? null : new RawImuData { SampleRate = rate,
                Meta = [.. sensors.Select(s => s.Meta)], ActiveLocations = [.. sensors.Select(s => s.Meta.LocationId)],
                Segments = [.. sensors.SelectMany(s => s.Finish())], HasGaps = recording.RecordingGap.HasValue },
            Front = front.Finish(), Rear = rear.Finish(), Airtimes = []
        };
        return new(telemetry, fixes.ToImmutable());
    }

    /// <summary>Front suspension velocity amplitude for a ground RMS level in g at a speed; the rear gets 0.8 of it.</summary>
    public static double ResponseAmplitude(double groundLevel, double speed) =>
        speed > 0 ? 140 * Math.Pow(groundLevel, 0.8) * Math.Pow(speed / 7, 0.7) : 0;

    private static bool Inside(TelemetryTimeRange? range, double seconds) =>
        range is { } r && seconds >= r.StartSeconds && seconds < r.EndSeconds;

    private static double Speed(double cruise, double seconds, RideRecording recording)
    {
        if (Inside(recording.Stop, seconds)) return 0;
        if (recording.Braking is not { } b) return cruise;
        var elapsed = seconds - b.StartSeconds;
        var reduction = Math.Clamp(Math.Min(elapsed, 2 * b.DurationSeconds - elapsed), 0, b.DurationSeconds);
        return Math.Max(0.1, cruise - recording.BrakingAcceleration * reduction);
    }

    private sealed class Sensor(ImuLocation location, int scale)
    {
        public ImuMetaEntry Meta { get; } = new((byte)location, scale, GyroScale);
        public TerrainSensor Identity => location switch
        {
            ImuLocation.Frame => TerrainSensor.FrameImu,
            ImuLocation.Fork => TerrainSensor.ForkImu,
            _ => TerrainSensor.RearAxleImu
        };
        private readonly List<RawImuSegment> segments = [];
        private readonly List<ImuRecord> samples = [];
        private int first;
        private int sampleRate;
        public double LevelScale(double speed) => location switch
        {
            ImuLocation.Frame => Math.Pow(speed / 7, 0.7),
            ImuLocation.Fork => ForkLevelFactor * Math.Pow(speed / 7, 0.9),
            _ => RearAxleLevelFactor * Math.Pow(speed / 7, 0.9)
        };

        public void Add(int tick, int rate, double ax, double ay, double az, double gx, double gy, double gz)
        {
            if (first + samples.Count != tick) Flush();
            if (samples.Count == 0) first = tick;
            sampleRate = rate;
            short Accel(double v) => (short)Math.Clamp(Math.Round(v * scale), -short.MaxValue, short.MaxValue);
            static short Gyro(double v) => (short)Math.Clamp(Math.Round(v * GyroScale), short.MinValue, short.MaxValue);
            samples.Add(new(Accel(ax), Accel(ay), Accel(az), Gyro(gx), Gyro(gy), Gyro(gz)));
        }

        public IReadOnlyList<RawImuSegment> Finish() { Flush(); return segments; }

        private void Flush()
        {
            if (samples.Count == 0) return;
            segments.Add(new RawImuSegment { LocationId = (byte)location, FirstIndex = (ulong)first,
                FirstMonotonicDeltaUs = (ulong)Math.Round(first * 1e6 / sampleRate), Records = [.. samples] });
            samples.Clear();
        }
    }

    private sealed class Side(int rate)
    {
        private readonly List<double> travel = [], velocity = [];
        private readonly List<ProcessedSuspensionSegment> segments = [];
        public void Add(int tick, double response, RideSection section, double bob)
        {
            var pedalAngle = 2 * Math.PI * section.PedalHz * tick / rate;
            if (segments.Count == 0 || tick != (int)segments[^1].FirstSourceIndex + travel.Count - segments[^1].FirstDenseIndex)
                segments.Add(new() { FirstDenseIndex = travel.Count, FirstSourceIndex = (ulong)tick, StartSeconds = (double)tick / rate });
            var compressionTime = section.ReboundGain / (section.CompressionGain + section.ReboundGain);
            var cycle = (2d * tick / rate) % 1;
            var compression = cycle < compressionTime;
            var angle = Math.PI * (compression ? cycle / compressionTime : (cycle - compressionTime) / (1 - compressionTime));
            velocity.Add(response * (compression ? section.CompressionGain : -section.ReboundGain) * Math.Sin(angle)
                + bob / 2 * 2 * Math.PI * section.PedalHz * Math.Cos(pedalAngle));
            travel.Add(35 + response * section.CompressionGain * compressionTime / (2 * Math.PI)
                * (1 + (compression ? -1 : 1) * Math.Cos(angle)) + bob / 2 * Math.Sin(pedalAngle));
        }

        public Suspension Finish()
        {
            var compressions = new List<Stroke>();
            var rebounds = new List<Stroke>();
            for (var i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var count = (i + 1 == segments.Count ? travel.Count : segments[i + 1].FirstDenseIndex) - seg.FirstDenseIndex;
                segments[i] = seg with { SampleCount = count };
                var strokes = StrokeAnalyzer.Analyze(velocity.GetRange(seg.FirstDenseIndex, count).ToArray(),
                    travel.GetRange(seg.FirstDenseIndex, count).ToArray(), 200, rate);
                Stroke Place(Stroke s) => s with { Start = seg.FirstDenseIndex + s.Start, End = seg.FirstDenseIndex + s.End,
                    StartSeconds = seg.StartSeconds + (double)s.Start / rate, EndSeconds = seg.StartSeconds + (double)s.End / rate };
                compressions.AddRange(strokes.Compressions.Select(Place));
                rebounds.AddRange(strokes.Rebounds.Select(Place));
            }
            return new Suspension { Present = true, Travel = [.. travel], Velocity = [.. velocity], MaxTravel = 200,
                HasGaps = segments.Count > 1, Segments = [.. segments],
                TravelBins = [.. HistogramBuilder.Linspace(0, 200, Parameters.TravelHistBins + 1)],
                VelocityBins = [.. HistogramBuilder.Linspace(-2000, 2000, 41)],
                Strokes = new Strokes { Compressions = [.. compressions], Rebounds = [.. rebounds] } };
        }
    }
}
