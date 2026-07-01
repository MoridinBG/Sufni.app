using System;
using BenchmarkDotNet.Attributes;
using Sufni.Telemetry;

[MemoryDiagnoser]
public class TelemetryDspBenchmarks
{
    private RawTelemetryData raw = null!;
    private Metadata metadata = null!;
    private BikeData bikeData;
    private SavitzkyGolay filter = null!;
    private double[] travel = null!;
    private double[] velocity = null!;
    private double[] h = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Deterministic ~5-minute ride at 1000 Hz => 300k samples/channel. Fixed seed:
        // the SAME dataset feeds the pre- and post-SIMD runs.
        const int sampleRate = 1000;
        const int samples = 300_000;
        var rng = new Random(20260701);
        var front = new ushort[samples];
        var rear = new ushort[samples];
        for (var i = 0; i < samples; i++)
        {
            var baseF = 2048 + 900 * Math.Sin(i / 180.0) + 300 * Math.Sin(i / 23.0);
            var baseR = 2048 + 700 * Math.Sin(i / 210.0 + 1.1) + 250 * Math.Sin(i / 29.0);
            front[i] = (ushort)Math.Clamp(baseF + rng.Next(-20, 21), 0.0, 4095.0);
            rear[i]  = (ushort)Math.Clamp(baseR + rng.Next(-20, 21), 0.0, 4095.0);
        }
        raw = new RawTelemetryData { Version = 4, SampleRate = sampleRate, Front = front, Rear = rear };
        metadata = new Metadata { SampleRate = sampleRate };
        bikeData = new BikeData(100.0, 100.0, v => v / 10.0, v => v / 10.0);

        // Focused inputs mirroring the pipeline's real filter (window 51, Filters/TelemetryData).
        filter = SavitzkyGolay.Create(51, 1, 3);
        travel = new double[samples];
        velocity = new double[samples];
        h = new double[samples];
        for (var i = 0; i < samples; i++)
        {
            travel[i] = front[i] / 10.0;
            velocity[i] = i > 0 ? (travel[i] - travel[i - 1]) * sampleRate : 0.0;
            h[i] = 1.0 / sampleRate;
        }
    }

    // Aggregate pipeline: "Allocated" captures the Strokes-ctor allocation removal in
    // context; "Mean" captures the overall SG + reduction speedup.
    [Benchmark]
    public TelemetryData FromRecording_EndToEnd() => TelemetryData.FromRecording(raw, metadata, bikeData);

    // Dominant SIMD win: Savitzky-Golay convolution -> TensorPrimitives.Dot.
    [Benchmark]
    public double[] SavitzkyGolay_Process() => filter.Process(travel, h);

    // Isolated allocation win: per-stroke Sum/Max/Min reductions (4 double[] -> 0 per stroke).
    [Benchmark]
    public double StrokeReductions()
    {
        double acc = 0.0;
        for (var start = 0; start + 1024 < travel.Length; start += 1024)
        {
            var stroke = new Stroke(start, start + 1023, 1.024, travel, velocity, 100.0);
            acc += stroke.Stat.SumTravel + stroke.Stat.MaxTravel;
        }
        return acc;
    }
}
