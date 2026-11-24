namespace Sufni.Telemetry;

public enum StrokeCharacter { BigHits, Chatter }

/// <summary>Distance-weighted shares of medium-or-rougher descent ground by median compression stroke size.</summary>
public sealed record CharacterShares(double RoughMetres, double BigHitShare, double ChatterShare)
{
    public const double MinimumMetres = 300;

    /// <summary>The character of most of the rough ground, from the minimum distance.</summary>
    public StrokeCharacter? Dominant => RoughMetres < MinimumMetres ? null
        : BigHitShare > 0.5 ? StrokeCharacter.BigHits
        : ChatterShare > 0.5 ? StrokeCharacter.Chatter : null;
}

/// <summary>
/// Experimental: whether rough ground made the suspension take big hits or chatter. The stroke-size thresholds come
/// from one bike's quartiles on rough ground and stay provisional until labelled rides confirm them.
/// </summary>
public sealed record TerrainCharacter(CharacterShares? Front, CharacterShares? Rear)
{
    private static readonly (double Chatter, double BigHits) FrontThresholds = (0.09, 0.20);
    private static readonly (double Chatter, double BigHits) RearThresholds = (0.08, 0.16);

    public static TerrainCharacter Describe(TerrainProfile profile, TelemetryTimeRange? range) => new(
        Shares(profile, range, profile.FrontMaxTravel, w => w.Front?.MedianStrokeSize, FrontThresholds),
        Shares(profile, range, profile.RearMaxTravel, w => w.Rear?.MedianStrokeSize, RearThresholds));

    private static CharacterShares? Shares(TerrainProfile profile, TelemetryTimeRange? range, double? maxTravel,
        Func<TerrainWindow, double?> strokeSize, (double Chatter, double BigHits) thresholds)
    {
        if (maxTravel is not > 0) return null;
        double metres = 0, bigHits = 0, chatter = 0;
        foreach (var window in profile.Windows)
        {
            if (!window.Moving || window.Climbing || window.Roughness is not (>= RoughnessClass.Medium and <= RoughnessClass.VeryRough) ||
                strokeSize(window) is not { } size) continue;
            var covered = TerrainProfileCalculator.WindowMetres * window.CoveredFraction(range);
            if (covered == 0) continue;
            metres += covered;
            var share = size / maxTravel.Value;
            if (share >= thresholds.BigHits) bigHits += covered;
            else if (share < thresholds.Chatter) chatter += covered;
        }
        return metres > 0 ? new(metres, bigHits / metres, chatter / metres) : null;
    }
}
