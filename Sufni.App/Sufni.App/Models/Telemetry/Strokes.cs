using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;

#pragma warning disable CS8618

namespace Sufni.App.Models.Telemetry;

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
public class Stroke
{
    public int Start { get; set; }
    public int End { get; set; }
    public StrokeStat Stat { get; set; }
    public int[] DigitizedTravel { get; set; }
    public int[] DigitizedVelocity { get; set; }
    public int[] FineDigitizedVelocity { get; set; }

    [IgnoreMember] public double Length { get; private set; }
    [IgnoreMember] public double Duration { get; set; }
    [IgnoreMember] public bool AirCandidate { get; set; }

    public Stroke() { }

    public Stroke(int start, int end, double duration, double[] travel, double[] velocity, double maxTravel)
    {
        Start = start;
        End = end;
        Length = travel[end] - travel[start];
        Duration = duration;

        var mv = Length < 0 ? velocity[start..(end + 1)].Min() : velocity[start..(end + 1)].Max();
        var bo = 0;
        for (var i = start; i < end; i++)
        {
            if (!(travel[i] > maxTravel - Parameters.BottomoutThreshold)) continue;
            bo += 1;
            for (; i < travel.Length && travel[i] > maxTravel - Parameters.BottomoutThreshold; i++) { }
        }

        Stat = new StrokeStat
        {
            SumTravel = travel[start..(end + 1)].Sum(),
            MaxTravel = travel[start..(end + 1)].Max(),
            SumVelocity = velocity[start..(end + 1)].Sum(),
            MaxVelocity = mv,
            Bottomouts = bo,
            Count = end - start + 1,
        };
    }

    public bool Overlaps(Stroke other)
    {
        var l = Math.Max(End - Start, other.End - other.Start);
        var s = Math.Max(Start, other.Start);
        var e = Math.Min(End, other.End);
        return e - s >= Parameters.AirtimeOverlapThreshold * l;
    }
};

[MessagePackObject(keyAsPropertyName: true)]
public class Strokes
{
    public Stroke[] Compressions { get; set; }
    public Stroke[] Rebounds { get; set; }
    [IgnoreMember] public Stroke[] Idlings { get; private set; }

    public void Categorize(Stroke[] strokes)
    {
        var compressions = new List<Stroke>();
        var rebounds = new List<Stroke>();
        var idlings = new List<Stroke>();

        for (var i = 0; i < strokes.Length; i++)
        {
            var stroke = strokes[i];
            if (Math.Abs(stroke.Length) < Parameters.StrokeLengthThreshold &&
                stroke.Duration >= Parameters.IdlingDurationThreshold)
            {
                // If suitable, tag this idling stroke as a possible airtime.
                // Whether or not it really is one will be decided with
                // further heuristics based on both front and rear
                // candidates.
                if (i > 0 && i < strokes.Length - 1 &&
                    stroke.Stat.MaxTravel <= Parameters.StrokeLengthThreshold &&
                    stroke.Duration >= Parameters.AirtimeDurationThreshold &&
                    strokes[i + 1].Stat.MaxVelocity >= Parameters.AirtimeVelocityThreshold)
                {
                    stroke.AirCandidate = true;
                }

                idlings.Add(stroke);
            }
            else if (stroke.Length >= Parameters.StrokeLengthThreshold)
            {
                compressions.Add(stroke);
            }
            else if (stroke.Length <= -Parameters.StrokeLengthThreshold)
            {
                rebounds.Add(stroke);
            }
        }

        Compressions = [.. compressions];
        Rebounds = [.. rebounds];
        Idlings = [.. idlings];
    }

    public void Digitize(int[] dt, int[] dv, int[] dvFine)
    {
        foreach (var s in Compressions)
        {
            s.DigitizedTravel = dt[s.Start..(s.End + 1)];
            s.DigitizedVelocity = dv[s.Start..(s.End + 1)];
            s.FineDigitizedVelocity = dvFine[s.Start..(s.End + 1)];
        }

        foreach (var s in Rebounds)
        {
            s.DigitizedTravel = dt[s.Start..(s.End + 1)];
            s.DigitizedVelocity = dv[s.Start..(s.End + 1)];
            s.FineDigitizedVelocity = dvFine[s.Start..(s.End + 1)];
        }
    }

    public static Stroke[] FilterStrokes(double[] velocity, double[] travel, double maxTravel, int sampleRate)
    {
        var strokes = new List<Stroke>();
        int velocityLength = velocity.Length;

        for (int i = 0; i < velocityLength - 1; i++)
        {
            int startIndex = i;
            int startSign = Math.Sign(velocity[i]);
            double maxPosition = travel[startIndex];

            // Loop until velocity changes sign
            while (i < velocityLength - 1 && Math.Sign(velocity[i + 1]) == startSign)
            {
                i++;
                if (travel[i] > maxPosition)
                {
                    maxPosition = travel[i];
                }
            }

            // We are at the end of the data stream
            if (i >= velocityLength)
            {
                i = velocityLength - 1;
            }

            // Top-out periods often oscillate a bit, so they are split into multiple
            // strokes. We fix this by concatenating consecutive strokes if their
            // mean position is close to zero.
            double duration = (i - startIndex + 1) / (double)sampleRate;
            if (maxPosition < Parameters.StrokeLengthThreshold &&
                strokes.Count > 0 &&
                strokes[^1].Stat.MaxTravel < Parameters.StrokeLengthThreshold)
            {
                strokes[^1].End = i;
                strokes[^1].Duration += duration;
            }
            else
            {
                strokes.Add(new Stroke(startIndex, i, duration, travel, velocity, maxTravel));
            }
        }

        return [.. strokes];
    }
};
