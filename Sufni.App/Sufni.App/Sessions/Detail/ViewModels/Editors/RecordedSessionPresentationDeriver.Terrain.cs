using System;
using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static partial class RecordedSessionPresentationDeriver
{
    public static TerrainPresentation CreateTerrain(ClassifiedProfile classified)
    {
        var roughness = ImmutableArray.CreateBuilder<TerrainRun<RoughnessClass>>();
        var grade = ImmutableArray.CreateBuilder<TerrainRun<GradeBand>>();
        var frame = new TerrainSignalSections();
        var fork = new TerrainSignalSections();
        var rearAxle = new TerrainSignalSections();
        var grades = new TerrainSignalSections();
        var bob = new TerrainSignalSections();
        int joint = 0, frameOnly = 0, forkOnly = 0;
        bool clipped = false, frameMissing = false, forkMissing = false, frameUncalibrated = false, forkUncalibrated = false;
        var profile = classified.Profile;
        for (var i = 0; i < profile.Windows.Length; i++)
        {
            var window = profile.Windows[i];
            if (!window.Moving) continue;
            AddRun(roughness, window, classified.Windows[i].Roughness);
            AddRun(grade, window, window.Band);
            var baseline = TerrainProfileCalculator.BaselineGrade(profile.Windows, i);
            frame.Add(window.StartSeconds, window.EndSeconds, window.Frame?.SpeedRemoved);
            fork.Add(window.StartSeconds, window.EndSeconds, window.Fork?.SpeedRemoved);
            rearAxle.Add(window.StartSeconds, window.EndSeconds, window.RearAxle?.SpeedRemoved);
            grades.Add(window.StartSeconds, window.EndSeconds, TerrainSignalSections.Degrees(baseline));
            bob.Add(window.StartSeconds, window.EndSeconds, window.Pedalling?.BobMillimetres);
            switch (classified.Windows[i].Source)
            {
                case RoughnessSource.Both: joint++; break;
                case RoughnessSource.FrameOnly:
                    frameOnly++;
                    clipped |= window.Fork?.ClippedShare > TerrainProfileCalculator.ClippedShareLimit;
                    forkMissing |= window.Fork is null;
                    forkUncalibrated |= window.Fork is { Normalised: null };
                    break;
                case RoughnessSource.ForkOnly:
                    forkOnly++;
                    frameMissing |= window.Frame is null;
                    frameUncalibrated |= window.Frame is { Normalised: null };
                    break;
            }
        }
        var total = Math.Max(1d, joint + frameOnly + forkOnly);
        return new(roughness.ToImmutable(), grade.ToImmutable(), frame.Build(), fork.Build(), grades.Build(),
            new(joint / total, frameOnly / total, forkOnly / total, clipped, frameMissing, forkMissing,
                frameUncalibrated, forkUncalibrated, profile.SilentSensors))
        {
            FrameEdges = Edges(ImuLocation.Frame, profile.FrameScale),
            ForkEdges = Edges(ImuLocation.Fork, profile.ForkScale),
            RearAxle = rearAxle.Build(),
            PedalBob = bob.Build()
        };

        static ImmutableArray<double> Edges(ImuLocation location, double? scale) =>
            scale is { } value && TerrainCalibration.For(location, value) is { } calibration ? [.. calibration.LevelEdges] : [];
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
