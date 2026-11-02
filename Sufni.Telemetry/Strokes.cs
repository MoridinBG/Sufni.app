using System.Numerics.Tensors;
using MessagePack;

#pragma warning disable CS8618

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public class StrokeStat
{
    public double SumTravel { get; set; }
    public double MaxTravel { get; set; }
    public double SumVelocity { get; set; }
    public double MaxVelocity { get; set; }
    public int Bottomouts { get; set; }
    public int Count { get; set; }
};

[MessagePackObject(keyAsPropertyName: true)]
[MessagePackFormatter(typeof(StrokeFormatter))]
public class Stroke
{
    #region Public properties

    public int Start { get; set; }
    public int End { get; set; }
    public StrokeStat Stat { get; set; }
    public int[] DigitizedTravel { get; set; } = [];
    public int[] DigitizedVelocity { get; set; } = [];
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }

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
public class Strokes
{
    #region Public properties

    public Stroke[] Compressions { get; set; }
    public Stroke[] Rebounds { get; set; }

    #endregion Public properties

}
