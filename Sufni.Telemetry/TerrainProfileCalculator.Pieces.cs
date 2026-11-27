using System.Collections.Immutable;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    // Penalties per piece: in class steps squared for each IMU, in degrees squared for grade. See terrain.md, Pieces.
    private const double RoughnessPiecePenalty = 4;
    private const double GradePiecePenalty = 400;
    private const int GradeMinimumPieceCells = 1;

    /// <summary>
    /// Roughness pieces cut on the speed-removed level of every given IMU together, each measured over its whole length.
    /// </summary>
    private static ImmutableArray<TerrainWindow> MeasureRoughnessPieces(RawImuData? imu, ImuMetaEntry?[] sensors,
        List<TerrainWindow> cells, CancellationToken token)
    {
        var used = sensors.OfType<ImuMetaEntry>().ToArray();
        if (used.Length == 0) return [];
        var signals = new List<double?[]>();
        foreach (var meta in used)
        {
            var measured = new List<TerrainWindow>(cells);
            MeasureSensor(imu, meta, measured, [], token);
            signals.Add([.. measured.Select(cell => ReadingOf(cell, (ImuLocation)meta.LocationId) is { SpeedRemoved: > 0 } reading
                ? Math.Log(reading.SpeedRemoved) / Math.Log(TerrainLevels.Step) : (double?)null)]);
        }
        var pieces = CutPieces(cells, signals, RoughnessPiecePenalty, CellsPerWindow);
        foreach (var meta in used) MeasureSensor(imu, meta, pieces, [], token);
        return [.. pieces];
    }

    private static SensorRoughness? ReadingOf(TerrainWindow window, ImuLocation location) => location switch
    {
        ImuLocation.Frame => window.Frame,
        ImuLocation.Fork => window.Fork,
        _ => window.RearAxle
    };

    /// <summary>
    /// Cuts each run of adjacent moving cells where the mean of the signals changes. A piece is at least
    /// <paramref name="minimumCells"/> long. A cell without a value takes the median of its run; a signal without values
    /// in a run does not cut it.
    /// </summary>
    private static List<TerrainWindow> CutPieces(List<TerrainWindow> cells, IReadOnlyList<double?[]> signals, double penalty,
        int minimumCells)
    {
        var pieces = new List<TerrainWindow>();
        var start = 0;
        for (var end = 1; end <= cells.Count; end++)
        {
            if (end < cells.Count && cells[end].Moving == cells[start].Moving && Adjacent(cells[end - 1], cells[end])) continue;
            if (cells[start].Moving)
            {
                var run = signals.Select(signal => Filled(signal, start, end)).OfType<double[]>().ToArray();
                foreach (var (from, to) in Cut(run, end - start, penalty * run.Length, minimumCells))
                {
                    var first = cells[start + from];
                    var last = cells[start + to - 1];
                    pieces.Add(new(first.StartMetres, first.StartSeconds, last.EndSeconds,
                        (to - from) * CellMetres / (last.EndSeconds - first.StartSeconds), true));
                }
            }
            start = end;
        }
        return pieces;
    }

    private static double[]? Filled(double?[] signal, int start, int end)
    {
        var values = signal[start..end];
        var present = values.OfType<double>().Order().ToArray();
        if (present.Length == 0) return null;
        var median = present[present.Length / 2];
        return [.. values.Select(v => v ?? median)];
    }

    /// <summary>
    /// The optimal partition by squared error from the segment means plus a penalty per piece (PELT), with pieces of at
    /// least <paramref name="minimum"/> cells. Pruning keeps it near linear in the number of cells.
    /// </summary>
    private static List<(int From, int To)> Cut(double[][] signals, int count, double penalty, int minimum)
    {
        if (count < 2 * minimum || signals.Length == 0) return [(0, count)];
        var sums = signals.Select(Prefix).ToArray();
        var squares = new double[count + 1];
        foreach (var signal in signals)
            for (var i = 0; i < count; i++) squares[i + 1] += signal[i] * signal[i];
        for (var i = 1; i <= count; i++) squares[i] += squares[i - 1];
        double Cost(int from, int to)
        {
            var cost = squares[to] - squares[from];
            foreach (var sum in sums) cost -= (sum[to] - sum[from]) * (sum[to] - sum[from]) / (to - from);
            return cost;
        }
        var best = new double[count + 1];
        var previous = new int[count + 1];
        best[0] = -penalty;
        var candidates = new List<int> { 0 };
        for (var to = minimum; to <= count; to++)
        {
            if (to - minimum >= minimum) candidates.Add(to - minimum);
            best[to] = double.PositiveInfinity;
            foreach (var from in candidates)
            {
                var total = best[from] + Cost(from, to) + penalty;
                if (total >= best[to]) continue;
                best[to] = total;
                previous[to] = from;
            }
            candidates.RemoveAll(from => best[from] + Cost(from, to) > best[to]);
        }
        var cuts = new List<(int, int)>();
        for (var to = count; to > 0; to = previous[to]) cuts.Add((previous[to], to));
        cuts.Reverse();
        return cuts;
    }

    private static double[] Prefix(double[] values)
    {
        var prefix = new double[values.Length + 1];
        for (var i = 0; i < values.Length; i++) prefix[i + 1] = prefix[i] + values[i];
        return prefix;
    }
}
