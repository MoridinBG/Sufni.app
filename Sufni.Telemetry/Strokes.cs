using System.Numerics.Tensors;
using MessagePack;

#pragma warning disable CS8618

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public sealed record StrokeStat
{
    public double SumTravel { get; init; }
    public double MaxTravel { get; init; }
    public double SumVelocity { get; init; }
    public double MaxVelocity { get; init; }
    public int Bottomouts { get; init; }
    public int Count { get; init; }
};

[MessagePackObject(keyAsPropertyName: true)]
[MessagePackFormatter(typeof(StrokeFormatter))]
public record Stroke
{
    #region Public properties

    public int Start { get; init; }
    public int End { get; init; }
    public StrokeStat Stat { get; init; }
    public int[] DigitizedTravel { get; init; } = [];
    public int[] DigitizedVelocity { get; init; } = [];
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }

    #endregion Public properties

    #region Constructors

    public Stroke() { }

    internal Stroke(int start, int end, double[] travel, double[] velocity, double maxTravel)
    {
        Start = start;
        End = end;
        var length = travel[end] - travel[start];

        var travelSpan = travel.AsSpan(start, end - start + 1);     // [start, end] inclusive
        var velocitySpan = velocity.AsSpan(start, end - start + 1);

        var mv = length < 0 ? TensorPrimitives.Min(velocitySpan) : TensorPrimitives.Max(velocitySpan);
        var bo = 0;
        for (var i = start; i < end; i++)
        {
            if (!(travel[i] > maxTravel - Parameters.BottomoutThreshold)) continue;
            bo += 1;
            for (; i < travel.Length && travel[i] > maxTravel - Parameters.BottomoutThreshold; i++) { }
        }

        Stat = new StrokeStat
        {
            SumTravel = TensorPrimitives.Sum(travelSpan),
            MaxTravel = TensorPrimitives.Max(travelSpan),
            SumVelocity = TensorPrimitives.Sum(velocitySpan),
            MaxVelocity = mv,
            Bottomouts = bo,
            Count = end - start + 1,
        };
    }

    #endregion Constructors

    #region Public methods

    public bool Overlaps(Stroke other)
    {
        if (EndSeconds > StartSeconds && other.EndSeconds > other.StartSeconds)
        {
            var duration = Math.Max(EndSeconds - StartSeconds, other.EndSeconds - other.StartSeconds);
            var start = Math.Max(StartSeconds, other.StartSeconds);
            var end = Math.Min(EndSeconds, other.EndSeconds);
            return end - start >= Parameters.AirtimeOverlapThreshold * duration;
        }

        var l = Math.Max(End - Start, other.End - other.Start);
        var s = Math.Max(Start, other.Start);
        var e = Math.Min(End, other.End);
        return e - s >= Parameters.AirtimeOverlapThreshold * l;
    }

    #endregion Public methods
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed record Strokes
{
    #region Public properties

    public Stroke[] Compressions { get; init; }
    public Stroke[] Rebounds { get; init; }

    #endregion Public properties

}
