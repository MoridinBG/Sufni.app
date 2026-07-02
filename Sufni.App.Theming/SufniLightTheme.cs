using static Sufni.App.Theming.SufniLightPalette;

namespace Sufni.App.Theming;

// Divergent token set for the light app theme. Shared composition lives in
// SufniThemeBuilder; this file supplies only the values that differ from dark.
public static class SufniLightTheme
{
    public static SufniTheme Instance { get; } = Create();

    private static SufniTheme Create()
    {
        var surface = new SufniSurfaceTheme(
            Page: Cloud,
            Elevated: Linen,
            Input: Porcelain,
            InputHover: Frost,
            InputPressed: Smoke,
            InputDisabled: Alabaster,
            ItemHover: Haze,
            InputFocused: Porcelain);
        var text = new SufniTextTheme(
            High: Jet,
            Emphasis: IronInk,
            Primary: Ink,
            Secondary: Anthracite,
            Hover: Tungsten,
            Disabled: Steel);
        var line = new SufniLineTheme(
            Subtle: Mineral,
            Default: Pewter,
            Divider: Limestone,
            Input: Stone,
            Strong: SlateDark,
            GridMinor: Frost);

        return SufniThemeBuilder.Build(new SufniThemeInputs(
            Mode: SufniThemeMode.Light,
            Surface: surface,
            Text: text,
            Line: line,
            AccentPrimary: AccentBlue,
            AccentPrimaryHover: AccentBlueLight,
            Danger: DangerRed,
            DangerDark: DangerRedDark,
            StatusWarning: WarningOchre,
            SelectionSurfaceSubtle: SelectionBlue,
            MapOverlaySurface: surface.Elevated,
            FieldBorderDisabled: Frost,
            DragHeader: Glacier,
            DropTargetHeader: DropTargetBlue,
            SignalRowRootPlotData: Vapor,
            SignalRowHostedLevel1: new SufniSignalRowDepthTheme(
                Container: Alabaster,
                Header: SandHeader,
                PlotFigure: SandFigure,
                PlotData: SandData),
            SignalRowHostedLevel2: new SufniSignalRowDepthTheme(
                Container: ClayContainer,
                Header: Stone,
                PlotFigure: ClayFigure,
                PlotData: ClayData),
            SignalRowHostedLevel3Plus: new SufniSignalRowDepthTheme(
                Container: EarthContainer,
                Header: EarthHeader,
                PlotFigure: EarthFigure,
                PlotData: EarthData),
            SignalRowConnector: SlateDark,
            PlotGridMajor: Pewter,
            PlotGridMinor: Smoke,
            PlotAxisLine: Pewter,
            PlotMarkerBlue: MarkerBlue,
            PlotAnalysisSelectionFrontBase: Indigo,
            PlotAnalysisSelectionRearBase: Lagoon,
            PlotDampingSelectionFill: WarningOchre.WithAlpha(0.20),
            PlotDampingSelectionOutline: WarningOchre.WithAlpha(0.62),
            PlotAnalysisSelectedFillOpacity: 0.2,
            PlotPreviewFill: AccentBlue.WithAlpha(0.24),
            PlotCursorLine: text.Secondary,
            PlotReferenceLine: SlateDark,
            OverlayScrim: SufniLightPalette.OverlayScrim,
            PlaceholderPreviewSurface: SufniLightPalette.PlaceholderPreviewSurface,
            MarkerRed: SufniLightPalette.MarkerRed));
    }
}
