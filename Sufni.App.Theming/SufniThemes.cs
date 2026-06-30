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

    // Typography and spacing are dimensions, not colors, and do not vary by
    // variant — both themes share these, and SufniThemeResourceBridge.PopulateRoot
    // writes them once as variant-invariant root resources.
    public static SufniTypographyTheme Typography { get; } = new(
        FontFamilyName: string.Empty,
        Size: new SufniFontSizeTheme(
            Caption: 11,
            Small: 12,
            Body: 13,
            Label: 14,
            Heading: 16,
            Display: 20),
        Body: new SufniTypographyRole(14, SufniThemeFontWeight.Regular),
        CompactLabel: new SufniTypographyRole(12, SufniThemeFontWeight.Regular),
        RowHeader: new SufniTypographyRole(14, SufniThemeFontWeight.SemiBold),
        AxisLabel: new SufniTypographyRole(14, SufniThemeFontWeight.Regular),
        AxisTick: new SufniTypographyRole(12, SufniThemeFontWeight.Regular),
        Legend: new SufniTypographyRole(12, SufniThemeFontWeight.Regular),
        ReadoutHeader: new SufniTypographyRole(13, SufniThemeFontWeight.SemiBold),
        ReadoutLine: new SufniTypographyRole(12, SufniThemeFontWeight.Regular),
        InPlotLabel: new SufniTypographyRole(13, SufniThemeFontWeight.Regular),
        Tab: new SufniTypographyRole(14, SufniThemeFontWeight.Medium),
        Action: new SufniTypographyRole(14, SufniThemeFontWeight.SemiBold),
        Placeholder: new SufniTypographyRole(14, SufniThemeFontWeight.Regular),
        FieldText: new SufniTypographyRole(14, SufniThemeFontWeight.Regular));

    public static SufniSpacingTheme Spacing { get; } = new(
        HierarchyIndent: 16,
        HeaderHorizontalPadding: 8,
        HeaderGlyphWidth: 20,
        ConnectorLineWidth: 2,
        ConnectorStemInsetFromGlyphLeft: 2,
        ConnectorGlyphGap: 6,
        ControlHeight: 39,
        BaseRowDividerHeight: 6,
        RootDropZoneHeight: 12);

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
