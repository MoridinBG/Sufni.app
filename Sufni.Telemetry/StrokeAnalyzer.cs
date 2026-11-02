namespace Sufni.Telemetry;

public static class StrokeAnalyzer
{
    public static Strokes Analyze(double[] velocity, double[] travel, double maxTravel, int sampleRate) =>
        AnalyzeCore(velocity, travel, maxTravel, sampleRate).Strokes;

    internal static StrokeAnalysis AnalyzeCore(double[] velocity, double[] travel, double maxTravel, int sampleRate)
    {
        var candidates = Filter(velocity, travel, maxTravel, sampleRate);
        var compressions = new List<Stroke>();
        var rebounds = new List<Stroke>();
        var idlings = new List<StrokeCandidate>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (Math.Abs(candidate.Length) < Parameters.StrokeLengthThreshold && candidate.Duration >= Parameters.IdlingDurationThreshold)
            {
                candidate.AirCandidate = i > 0 && i < candidates.Count - 1 &&
                    candidate.Stat.MaxTravel <= Parameters.StrokeLengthThreshold &&
                    candidate.Duration >= Parameters.AirtimeDurationThreshold &&
                    candidates[i + 1].Stat.MaxVelocity >= Parameters.AirtimeVelocityThreshold;
                idlings.Add(candidate);
            }
            else if (candidate.Length >= Parameters.StrokeLengthThreshold) compressions.Add(candidate.Publish());
            else if (candidate.Length <= -Parameters.StrokeLengthThreshold) rebounds.Add(candidate.Publish());
        }
        return new(new Strokes { Compressions = [.. compressions], Rebounds = [.. rebounds] }, [.. idlings]);
    }

    private static List<StrokeCandidate> Filter(double[] velocity, double[] travel, double maxTravel, int sampleRate)
    {
        var strokes = new List<StrokeCandidate>();
        for (var i = 0; i < velocity.Length - 1; i++)
        {
            var start = i;
            var sign = Math.Sign(velocity[i]);
            var maxPosition = travel[start];
            while (i < velocity.Length - 1 && Math.Sign(velocity[i + 1]) == sign)
            {
                i++;
                if (travel[i] > maxPosition) maxPosition = travel[i];
            }
            var duration = (i - start + 1) / (double)sampleRate;
            // Keep top-out concatenation's original aggregate statistics and travel delta.
            if (maxPosition < Parameters.StrokeLengthThreshold && strokes.Count > 0 &&
                strokes[^1].Stat.MaxTravel < Parameters.StrokeLengthThreshold)
            {
                strokes[^1].End = i;
                strokes[^1].Duration += duration;
                strokes[^1].EndSeconds = i / (double)sampleRate;
            }
            else
            {
                strokes.Add(new StrokeCandidate(start, i, duration, travel, velocity, maxTravel)
                {
                    StartSeconds = start / (double)sampleRate,
                    EndSeconds = i / (double)sampleRate,
                });
            }
        }
        return strokes;
    }
}

internal sealed record StrokeAnalysis(Strokes Strokes, StrokeCandidate[] Idlings);

internal sealed class StrokeCandidate : Stroke
{
    public double Length { get; }
    public double Duration { get; set; }
    public bool AirCandidate { get; set; }

    public StrokeCandidate(int start, int end, double duration, double[] travel, double[] velocity, double maxTravel)
        : base(start, end, travel, velocity, maxTravel)
    {
        Length = travel[end] - travel[start];
        Duration = duration;
    }

    public Stroke Publish() => new()
    {
        Start = Start, End = End, Stat = Stat, StartSeconds = StartSeconds, EndSeconds = EndSeconds,
    };
}
