using static Sufni.App.Theming.SufniDarkPalette;

namespace Sufni.App.Theming;

// Divergent token set for the dark app theme. Shared composition lives in
// SufniThemeBuilder; this file supplies only the values that differ from light.
public static class SufniDarkTheme
{
    public static SufniTheme Instance { get; } = Create();

    private static SufniTheme Create()
    {
        var surface = new SufniSurfaceTheme(
            Page: Obsidian,
            Elevated: SlateBlack,
            Input: SlateAbyss,
            InputHover: Slate,
            InputPressed: Slate,
            InputDisabled: SlateMidnight,
            ItemHover: SlateNight,
            InputFocused: Onyx);
        var text = new SufniTextTheme(
            High: PureWhite,
            Emphasis: Snow,
            Primary: Fog,
            Secondary: Ash,
            Hover: Silver,
            Disabled: Graphite);
        var line = new SufniLineTheme(
            Subtle: SlateCharcoal,
            Default: SteelGray,
            Divider: Charcoal,
            Input: DarkIron,
            Strong: Iron,
            GridMinor: Gunmetal);

        return SufniThemeBuilder.Build(new SufniThemeInputs(
            Mode: SufniThemeMode.Dark,
            Surface: surface,
            Text: text,
            Line: line,
            AccentPrimary: AccentBlue,
            AccentPrimaryHover: AccentBlueLight,
            Danger: DangerRed,
            DangerDark: DangerRedDark,
            StatusWarning: WarningGold,
            SelectionSurfaceSubtle: SlateMidnight,
            MapOverlaySurface: surface.Input,
            FieldBorderDisabled: Coal,
            DragHeader: DragHeader,
            DropTargetHeader: DropTargetTeal,
            GraphRowRootPlotData: surface.Input,
            GraphRowHostedLevel1: new SufniGraphRowDepthTheme(
                Container: TarContainer,
                Header: TarHeader,
                PlotFigure: TarFigure,
                PlotData: TarData),
            GraphRowHostedLevel2: new SufniGraphRowDepthTheme(
                Container: PitchContainer,
                Header: PitchHeader,
                PlotFigure: PitchFigure,
                PlotData: PitchData),
            GraphRowHostedLevel3Plus: new SufniGraphRowDepthTheme(
                Container: VoidContainer,
                Header: VoidHeader,
                PlotFigure: VoidFigure,
                PlotData: VoidData),
            GraphRowConnector: SlateGray,
            PlotGridMajor: SteelGray,
            PlotGridMinor: Gunmetal,
            PlotAxisLine: SteelGray,
            PlotMarkerBlue: MarkerBlue,
            PlotStatisticsSelectionFrontBase: Indigo,
            PlotStatisticsSelectionRearBase: Lagoon,
            PlotDampingSelectionFill: WarningGold.WithAlpha(0.22),
            PlotDampingSelectionOutline: WarningGold.WithAlpha(0.65),
            PlotAnalysisSelectedFillOpacity: 0.16,
            PlotPreviewFill: SlateMist.WithAlpha(0.12),
            PlotCursorLine: Pearl,
            PlotReferenceLine: Mist,
            OverlayScrim: SufniDarkPalette.OverlayScrim,
            PlaceholderPreviewSurface: SufniDarkPalette.PlaceholderPreviewSurface,
            MarkerRed: SufniDarkPalette.MarkerRed));
    }
}
