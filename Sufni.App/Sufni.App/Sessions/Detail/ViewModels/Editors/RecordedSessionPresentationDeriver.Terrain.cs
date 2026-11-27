using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static partial class RecordedSessionPresentationDeriver
{
    public static TerrainPresentation CreateTerrain(ClassifiedProfile classified)
    {
        var profile = classified.Profile;
        var scale = classified.Scale;
        var roughness = ImmutableArray.CreateBuilder<TerrainRun<RoughnessClass>>();
        var grade = ImmutableArray.CreateBuilder<TerrainRun<GradeBand>>();
        var roughnessShades = ImmutableArray.CreateBuilder<TerrainShade<RoughnessClass>>();
        var gradeShades = ImmutableArray.CreateBuilder<TerrainShade<GradeBand>>();
        var frame = new TerrainSignalSections();
        var fork = new TerrainSignalSections();
        var rearAxle = new TerrainSignalSections();
        var grades = new TerrainSignalSections();
        var bob = new TerrainSignalSections();
        var shares = new double[Enum.GetValues<GroundSensors>().Length];
        bool forkClipped = false, rearAxleClipped = false, frameMissing = false, forkMissing = false, rearAxleMissing = false,
            climbReading = false;
        double classed = 0, labelled = 0;
        foreach (var window in profile.Windows)
        {
            if (!window.Moving) continue;
            frame.Add(window.StartSeconds, window.EndSeconds, window.Frame?.SpeedRemoved);
            fork.Add(window.StartSeconds, window.EndSeconds, window.Fork?.SpeedRemoved);
            rearAxle.Add(window.StartSeconds, window.EndSeconds, window.RearAxle?.SpeedRemoved);
            bob.Add(window.StartSeconds, window.EndSeconds, window.Pedalling?.BobMillimetres);
        }
        foreach (var classes in classified.Pieces)
        {
            var piece = classes.Window;
            if (!piece.Moving) continue;
            AddRun(roughness, piece, classes.Roughness);
            AddRun(grade, piece, piece.Band);
            if (classes.Roughness is not null)
            {
                classed += piece.Metres;
                if (classes.Labelled) labelled += piece.Metres;
            }
            var degrees = TerrainSignalSections.Degrees(piece.Grade);
            AddShade(roughnessShades, piece, classes.Roughness, !classes.Labelled && classes.Roughness?.IsClimb() == false && classes.ClassValue is { } value
                ? Math.Round(Math.Clamp(value, -1.5, 2.5) * 4, MidpointRounding.AwayFromZero) / 4 : null);
            AddShade(gradeShades, piece, piece.Band, !classes.GradeLabelled && degrees is { } shade
                ? Math.Round(Math.Clamp(shade, -8, 17.5), MidpointRounding.AwayFromZero) : null);
            grades.Add(piece.StartSeconds, piece.EndSeconds, degrees);
            if (piece.Climbing)
            {
                climbReading |= piece.Frame is not null;
                continue;
            }
            if (classes.Source is not { } source) continue;
            shares[(int)source] += piece.Metres;
            forkClipped |= LeftOutClipped(piece.Fork, scale.ForkImu, source is GroundSensors.Fork or GroundSensors.ForkAndRearAxle);
            rearAxleClipped |= LeftOutClipped(piece.RearAxle, scale.RearAxleImu,
                source is GroundSensors.RearAxle or GroundSensors.ForkAndRearAxle);
            frameMissing |= piece.Frame is null;
            forkMissing |= piece.Fork is null;
            rearAxleMissing |= piece.RearAxle is null;
        }

        var total = Math.Max(1d, shares.Sum());
        return new(roughness.ToImmutable(), grade.ToImmutable(), frame.Build(), fork.Build(), grades.Build(),
            new([.. shares.Select(count => count / total)], forkClipped, rearAxleClipped, frameMissing, forkMissing,
                rearAxleMissing, climbReading, profile.SilentSensors,
                labelled / Math.Max(1d, classed)))
        {
            RoughnessShades = roughnessShades.ToImmutable(),
            GradeShades = gradeShades.ToImmutable(),
            RoughnessLabelRuns = LabelRuns(classified.Labels.Ranges.Select(range => (range.StartSeconds, range.EndSeconds, range.Class))),
            SteepnessLabelRuns = LabelRuns(classified.Labels.Grades.Select(range => (range.StartSeconds, range.EndSeconds, range.Band))),
            FrameEdges = Edges(scale.FrameImu),
            ForkEdges = Edges(scale.ForkImu),
            RearAxleEdges = Edges(scale.RearAxleImu),
            RearAxle = rearAxle.Build(),
            PedalBob = bob.Build()
        };

        static ImmutableArray<TerrainRun<TClass>> LabelRuns<TClass>(IEnumerable<(double Start, double End, TClass Class)> ranges)
            where TClass : struct, Enum
        {
            var runs = ImmutableArray.CreateBuilder<TerrainRun<TClass>>();
            foreach (var (start, end, labelClass) in ranges)
            {
                var from = runs.Count > 0 ? Math.Max(start, runs[^1].EndSeconds) : start;
                if (from < end) runs.Add(new(from, end, labelClass));
            }
            return runs.ToImmutable();
        }

        static ImmutableArray<double> Edges(int? medianBin) => medianBin is { } bin ? [.. TerrainLevels.Edges(bin)] : [];

        static bool LeftOutClipped(SensorRoughness? reading, int? medianBin, bool inClassValue) => !inClassValue &&
            medianBin is not null && reading is { Level: > 0 } && reading.ClippedShare > TerrainProfileCalculator.ClippedShareLimit;
    }

    public static RecordedSessionEditorState WithTerrainSelection(RecordedSessionEditorState previous, RecordedSessionEditorState next)
    {
        var classified = (next.Terrain as RecordedTerrainState.Loaded)?.Classified;
        if (ReferenceEquals(classified, (previous.Terrain as RecordedTerrainState.Loaded)?.Classified) &&
            next.TerrainScope == previous.TerrainScope && next.Intent.AnalysisRange == previous.Intent.AnalysisRange)
            return next;
        return next with
        {
            TerrainSelection = classified is null ? null : TerrainSelectionQuery.Evaluate(classified, next.TerrainScope!, next.Intent.AnalysisRange)
        };
    }

    private static void AddRun<TClass>(ImmutableArray<TerrainRun<TClass>>.Builder runs, TerrainWindow window, TClass? value)
        where TClass : struct, Enum
    {
        if (value is not { } current) return;
        if (runs.Count > 0 && runs[^1] is var last && last.Class.Equals(current) && last.EndSeconds == window.StartSeconds)
            runs[^1] = last with { EndSeconds = window.EndSeconds };
        else
            runs.Add(new(window.StartSeconds, window.EndSeconds, current));
    }

    private static void AddShade<TClass>(ImmutableArray<TerrainShade<TClass>>.Builder shades, TerrainWindow window, TClass? value,
        double? shade) where TClass : struct, Enum
    {
        if (value is not { } current) return;
        if (shades.Count > 0 && shades[^1] is var last && last.Class.Equals(current) && last.Value == shade &&
            last.EndSeconds == window.StartSeconds)
            shades[^1] = last with { EndSeconds = window.EndSeconds };
        else
            shades.Add(new(window.StartSeconds, window.EndSeconds, current, shade));
    }
}
