using System;
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
        var frame = new TerrainSignalSections();
        var fork = new TerrainSignalSections();
        var rearAxle = new TerrainSignalSections();
        var grades = new TerrainSignalSections();
        var bob = new TerrainSignalSections();
        var shares = new double[Enum.GetValues<GroundSensors>().Length];
        bool forkClipped = false, rearAxleClipped = false, frameMissing = false, forkMissing = false, rearAxleMissing = false,
            climbReading = false;
        for (var i = 0; i < profile.Windows.Length; i++)
        {
            var window = profile.Windows[i];
            if (!window.Moving) continue;
            var classes = classified.Windows[i];
            AddRun(roughness, window, classes.Roughness);
            AddRun(grade, window, window.Band);
            var baseline = TerrainProfileCalculator.BaselineGrade(profile.Windows, i);
            frame.Add(window.StartSeconds, window.EndSeconds, window.Frame?.SpeedRemoved);
            fork.Add(window.StartSeconds, window.EndSeconds, window.Fork?.SpeedRemoved);
            rearAxle.Add(window.StartSeconds, window.EndSeconds, window.RearAxle?.SpeedRemoved);
            grades.Add(window.StartSeconds, window.EndSeconds, TerrainSignalSections.Degrees(baseline));
            bob.Add(window.StartSeconds, window.EndSeconds, window.Pedalling?.BobMillimetres);
            if (window.Climbing)
            {
                climbReading |= window.Frame is not null;
                continue;
            }
            if (classes.Source is not { } source) continue;
            shares[(int)source]++;
            forkClipped |= LeftOutClipped(window.Fork, scale.ForkImu, source is GroundSensors.Fork or GroundSensors.ForkAndRearAxle);
            rearAxleClipped |= LeftOutClipped(window.RearAxle, scale.RearAxleImu,
                source is GroundSensors.RearAxle or GroundSensors.ForkAndRearAxle);
            frameMissing |= window.Frame is null;
            forkMissing |= window.Fork is null;
            rearAxleMissing |= window.RearAxle is null;
        }
        var total = Math.Max(1d, shares.Sum());
        return new(roughness.ToImmutable(), grade.ToImmutable(), frame.Build(), fork.Build(), grades.Build(),
            new([.. shares.Select(count => count / total)], forkClipped, rearAxleClipped, frameMissing, forkMissing,
                rearAxleMissing, climbReading, profile.SilentSensors))
        {
            FrameEdges = Edges(scale.FrameImu),
            ForkEdges = Edges(scale.ForkImu),
            RearAxleEdges = Edges(scale.RearAxleImu),
            RearAxle = rearAxle.Build(),
            PedalBob = bob.Build()
        };

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
}
