using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>A profile window with its ground class; the class value is the normalised level the class came from.</summary>
public sealed record ClassifiedWindow(TerrainWindow Window, RoughnessClass? Roughness, RoughnessSource? Source, double? ClassValue);

/// <summary>A profile with one classified window per profile window, in the same order.</summary>
public sealed record ClassifiedProfile(TerrainProfile Profile, ImmutableArray<ClassifiedWindow> Windows);

public static class TerrainClassifier
{
    public static ClassifiedProfile Classify(TerrainProfile profile) =>
        new(profile, [.. profile.Windows.Select(w => new ClassifiedWindow(w, w.Roughness, w.Source, w.NormalisedRoughness))]);
}
