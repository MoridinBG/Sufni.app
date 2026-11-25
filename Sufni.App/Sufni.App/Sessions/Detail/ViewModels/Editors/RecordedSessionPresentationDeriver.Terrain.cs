using System;
using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static partial class RecordedSessionPresentationDeriver
{
    public static TerrainPresentation CreateTerrain(TerrainProfile profile)
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
        for (var i = 0; i < profile.Windows.Length; i++)
        {
            var window = profile.Windows[i];
            if (!window.Moving) continue;
            AddRun(roughness, window, window.Roughness);
            AddRun(grade, window, window.Band);
            var baseline = TerrainProfileCalculator.BaselineGrade(profile.Windows, i);
            frame.Add(window.StartSeconds, window.EndSeconds, window.Frame?.SpeedRemoved);
            fork.Add(window.StartSeconds, window.EndSeconds, window.Fork?.SpeedRemoved);
            rearAxle.Add(window.StartSeconds, window.EndSeconds, window.RearAxle?.SpeedRemoved);
            grades.Add(window.StartSeconds, window.EndSeconds, TerrainSignalSections.Degrees(baseline));
            bob.Add(window.StartSeconds, window.EndSeconds, window.Pedalling?.BobMillimetres);
            switch (window.Source)
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
        var profile = (next.Terrain as RecordedTerrainState.Loaded)?.Profile;
        if (ReferenceEquals(profile, (previous.Terrain as RecordedTerrainState.Loaded)?.Profile) &&
            next.TerrainScope == previous.TerrainScope && next.Intent.AnalysisRange == previous.Intent.AnalysisRange)
            return next;
        return next with
        {
            TerrainSelection = profile is null ? null : TerrainSelectionQuery.Evaluate(profile, next.TerrainScope!, next.Intent.AnalysisRange)
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
