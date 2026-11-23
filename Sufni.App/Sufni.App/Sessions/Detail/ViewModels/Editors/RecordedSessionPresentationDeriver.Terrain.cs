using System;
using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static partial class RecordedSessionPresentationDeriver
{
    public static TerrainPresentation CreateTerrain(TerrainProfile profile)
    {
        var roughness = ImmutableArray.CreateBuilder<TerrainClassRun>();
        var steepness = ImmutableArray.CreateBuilder<TerrainClassRun>();
        var frame = new TerrainSignalSections();
        var lowers = new TerrainSignalSections();
        var grades = new TerrainSignalSections();
        int joint = 0, frameOnly = 0, lowersOnly = 0;
        bool clipped = false, frameMissing = false, lowersMissing = false, frameUncalibrated = false, lowersUncalibrated = false;
        for (var i = 0; i < profile.Windows.Length; i++)
        {
            var window = profile.Windows[i];
            if (!window.Moving) continue;
            AddRun(roughness, window, window.Roughness switch
            {
                RoughnessClass.Calm => TerrainLevel.Low, RoughnessClass.Medium => TerrainLevel.Middle,
                RoughnessClass.Rough => TerrainLevel.High, _ => null
            });
            AddRun(steepness, window, window.Steepness switch
            {
                SteepnessClass.Gentle => TerrainLevel.Low, SteepnessClass.Moderate => TerrainLevel.Middle,
                SteepnessClass.Steep => TerrainLevel.High, _ => null
            });
            var grade = TerrainProfileCalculator.BaselineGrade(profile.Windows, i);
            frame.Add(window.StartSeconds, window.EndSeconds, window.Frame?.SpeedRemoved);
            lowers.Add(window.StartSeconds, window.EndSeconds, window.Lowers?.SpeedRemoved);
            grades.Add(window.StartSeconds, window.EndSeconds, TerrainSignalSections.Degrees(grade));
            switch (window.Source)
            {
                case RoughnessSource.Both: joint++; break;
                case RoughnessSource.FrameOnly:
                    frameOnly++;
                    clipped |= window.Lowers?.ClippedShare > TerrainProfileCalculator.ClippedShareLimit;
                    lowersMissing |= window.Lowers is null;
                    lowersUncalibrated |= window.Lowers is { Normalised: null };
                    break;
                case RoughnessSource.LowersOnly:
                    lowersOnly++;
                    frameMissing |= window.Frame is null;
                    frameUncalibrated |= window.Frame is { Normalised: null };
                    break;
            }
        }
        var total = Math.Max(1d, joint + frameOnly + lowersOnly);
        return new(roughness.ToImmutable(), steepness.ToImmutable(), frame.Build(), lowers.Build(), grades.Build(),
            new(joint / total, frameOnly / total, lowersOnly / total, clipped, frameMissing, lowersMissing,
                frameUncalibrated, lowersUncalibrated));
    }

    private static void AddRun(ImmutableArray<TerrainClassRun>.Builder runs, TerrainWindow window, TerrainLevel? level)
    {
        if (level is not { } value) return;
        if (runs.Count > 0 && runs[^1] is var last && last.Level == value && last.EndSeconds == window.StartSeconds)
            runs[^1] = last with { EndSeconds = window.EndSeconds };
        else
            runs.Add(new(window.StartSeconds, window.EndSeconds, value));
    }
}
