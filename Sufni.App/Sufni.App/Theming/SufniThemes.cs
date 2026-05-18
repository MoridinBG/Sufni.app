using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Styling;

namespace Sufni.App.Theming;

// Central access point for concrete themes and theme-invariant signal palettes.
public static class SufniThemes
{
    private static Color C(string hex) => Color.Parse(hex);

    // Travel-zone colors are data semantics, so they do not vary by theme.
    public static IReadOnlyList<Color> TravelZoneRamp { get; } =
    [
        C("#3288BD"),
        C("#66C2A5"),
        C("#ABDDA4"),
        C("#E6F598"),
        C("#FFFFBF"),
        C("#FEE08B"),
        C("#FDAE61"),
        C("#F46D43"),
        C("#D53E4F"),
        C("#9E0142"),
    ];

    // Core telemetry series colors stay stable so plots compare consistently.
    public static SufniSeriesTheme SignalSeries { get; } = new(
        SuspensionFront: C("#3288BD"),
        SuspensionRear: C("#66C2A5"),
        ImuFrame: C("#FC8D59"),
        ImuFork: C("#3288BD"),
        ImuShock: C("#66C2A5"),
        GpsSpeed: C("#FFFFBF"),
        GpsElevation: C("#FFFFBF"),
        GpsQuality: C("#D0D6DA"),
        TravelZone: TravelZoneRamp);

    // Used only where static metadata needs a value before a visual has a variant.
    public static SufniTheme Fallback => Dark;

    public static SufniTheme Dark => SufniDarkTheme.Instance;

    public static SufniTheme Light => SufniLightTheme.Instance;

    public static SufniTheme FromMode(SufniThemeMode mode)
        => mode == SufniThemeMode.Light ? Light : Dark;

    public static SufniTheme FromVariant(ThemeVariant? variant)
        => variant == ThemeVariant.Light ? Light : Dark;

    public static ThemeVariant ToVariant(SufniThemeMode mode)
        => mode switch
        {
            SufniThemeMode.Light => ThemeVariant.Light,
            SufniThemeMode.System => ThemeVariant.Default,
            _ => ThemeVariant.Dark
        };

    public static SufniThemeMode EffectiveModeFromVariant(ThemeVariant? variant)
        => variant == ThemeVariant.Light ? SufniThemeMode.Light : SufniThemeMode.Dark;
}
