using Avalonia.Media;

namespace Sufni.App.Theming;

// The values that genuinely differ between the dark and light themes. Everything
// else — cross-references, opacities, dimensions, and the fixed marker alphas — is
// composed once in SufniThemeBuilder, so the two themes cannot drift structurally.
public sealed record SufniThemeInputs(
    SufniThemeMode Mode,
    SufniSurfaceTheme Surface,
    SufniTextTheme Text,
    SufniLineTheme Line,
    Color AccentPrimary,
    Color AccentPrimaryHover,
    Color Danger,
    Color DangerDark,
    Color StatusWarning,
    Color SelectionSurfaceSubtle,
    Color MapOverlaySurface,
    Color FieldBorderDisabled,
    Color DragHeader,
    Color DropTargetHeader,
    Color GraphRowRootPlotData,
    SufniGraphRowDepthTheme GraphRowHostedLevel1,
    SufniGraphRowDepthTheme GraphRowHostedLevel2,
    SufniGraphRowDepthTheme GraphRowHostedLevel3Plus,
    Color GraphRowConnector,
    Color PlotGridMajor,
    Color PlotGridMinor,
    Color PlotAxisLine,
    Color PlotMarkerBlue,
    Color PlotStatisticsSelectionFrontBase,
    Color PlotStatisticsSelectionRearBase,
    Color PlotDampingSelectionFill,
    Color PlotDampingSelectionOutline,
    double PlotAnalysisSelectedFillOpacity,
    Color PlotPreviewFill,
    Color PlotCursorLine,
    Color PlotReferenceLine,
    Color OverlayScrim,
    Color PlaceholderPreviewSurface,
    Color MarkerRed);

// Single composition point for both app themes. Given a theme's divergent inputs it
// derives every cross-referenced token, applies the fixed opacities/dimensions, and
// assembles the SufniTheme. The plot depth themes are derived from the graph-row depth
// themes (they were always equal), so that invariant is now enforced by construction.
public static class SufniThemeBuilder
{
    public static SufniTheme Build(SufniThemeInputs inputs)
    {
        var surface = inputs.Surface;
        var text = inputs.Text;
        var line = inputs.Line;

        var action = new SufniActionTheme(
            AccentPrimary: inputs.AccentPrimary,
            AccentPrimaryHover: inputs.AccentPrimaryHover,
            Danger: inputs.Danger,
            DangerDark: inputs.DangerDark,
            AccentAliases: new SufniAccentAliasTheme(
                TabUnderline: inputs.AccentPrimary,
                NavPipe: inputs.AccentPrimary,
                DropPosition: inputs.AccentPrimary,
                FocusRing: inputs.AccentPrimary,
                Hyperlink: inputs.AccentPrimary,
                Spinner: inputs.AccentPrimary),
            Disabled: new SufniDisabledActionTheme(surface.InputDisabled, text.Disabled, 0.3));

        var selection = new SufniSelectionTheme(
            SurfaceSubtle: inputs.SelectionSurfaceSubtle,
            Indicator: action.AccentPrimary,
            IndicatorThickness: 2,
            IndicatorLengthVertical: 60);

        var tab = new SufniTabTheme(
            Text: text.Secondary,
            TextSelected: text.Primary,
            TextHover: text.Hover,
            SurfaceHover: surface.ItemHover,
            SurfaceSelected: selection.SurfaceSubtle,
            Indicator: selection.Indicator,
            IndicatorBleed: 12,
            WindowFontSize: 16,
            StatisticsFontSize: 16,
            NavFontSize: 9);

        var navRail = new SufniNavRailTheme(
            CompactPaneWidth: 51,
            Background: surface.Page,
            BottomBackground: selection.SurfaceSubtle,
            BorderRight: surface.InputDisabled);

        var list = new SufniListTheme(
            RowSurface: surface.Page,
            RowSurfaceHover: surface.ItemHover,
            RowDivider: line.Divider);

        var searchBar = new SufniSearchBarTheme(
            Surface: surface.Input,
            CornerRadius: 5,
            BorderThickness: 0,
            Height: 39);

        var mapOverlay = new SufniMapOverlayTheme(
            Surface: inputs.MapOverlaySurface,
            Opacity: 0.9,
            CornerRadius: 5,
            Padding: 5,
            Text: text.Primary);

        var splitter = new SufniSplitterTheme(
            Surface: line.Divider,
            MinThickness: 3);

        var field = new SufniFieldTheme(
            Label: text.Secondary,
            Value: text.Primary,
            Surface: surface.Input,
            SurfaceFocused: surface.InputFocused,
            Border: line.Input,
            BorderFocused: action.AccentPrimary,
            BorderDisabled: inputs.FieldBorderDisabled,
            Height: 39);

        var dragDrop = new SufniDragDropTheme(
            Header: inputs.DragHeader,
            FeedbackOpacity: 0.72,
            DropTargetHeader: inputs.DropTargetHeader,
            DropPositionIndicator: action.AccentAliases.DropPosition);

        var graphRow = new SufniGraphRowTheme(
            Root: new SufniGraphRowDepthTheme(
                Container: surface.Page,
                Header: surface.Elevated,
                PlotFigure: surface.Page,
                PlotData: inputs.GraphRowRootPlotData),
            HostedLevel1: inputs.GraphRowHostedLevel1,
            HostedLevel2: inputs.GraphRowHostedLevel2,
            HostedLevel3Plus: inputs.GraphRowHostedLevel3Plus,
            Connector: inputs.GraphRowConnector,
            DividerBetweenRoots: line.Divider);

        var series = SufniThemes.SignalSeries;

        // Plot figure/data backgrounds mirror the graph-row depth themes at every level.
        var plot = new SufniPlotTheme(
            Root: new SufniPlotDepthTheme(graphRow.Root.PlotFigure, graphRow.Root.PlotData),
            HostedLevel1: new SufniPlotDepthTheme(graphRow.HostedLevel1.PlotFigure, graphRow.HostedLevel1.PlotData),
            HostedLevel2: new SufniPlotDepthTheme(graphRow.HostedLevel2.PlotFigure, graphRow.HostedLevel2.PlotData),
            HostedLevel3Plus: new SufniPlotDepthTheme(graphRow.HostedLevel3Plus.PlotFigure, graphRow.HostedLevel3Plus.PlotData),
            Grid: new SufniPlotGridTheme(inputs.PlotGridMajor, inputs.PlotGridMinor),
            Axis: new SufniPlotAxisTheme(inputs.PlotAxisLine, text.Primary, text.Primary),
            Legend: new SufniPlotLegendTheme(surface.Elevated, line.Subtle, text.Primary),
            Marker: new SufniPlotMarkerTheme(
                Line: inputs.PlotMarkerBlue.WithAlpha(0.9),
                AirtimeFill: inputs.PlotMarkerBlue.WithAlpha(0.2),
                AirtimeOutline: text.Secondary.WithAlpha(0.5),
                StatisticsSelectionFrontFill: inputs.PlotStatisticsSelectionFrontBase.WithAlpha(0.20),
                StatisticsSelectionFrontOutline: inputs.PlotStatisticsSelectionFrontBase.WithAlpha(0.66),
                StatisticsSelectionRearFill: inputs.PlotStatisticsSelectionRearBase.WithAlpha(0.20),
                StatisticsSelectionRearOutline: inputs.PlotStatisticsSelectionRearBase.WithAlpha(0.66),
                DampingSelectionFill: inputs.PlotDampingSelectionFill,
                DampingSelectionOutline: inputs.PlotDampingSelectionOutline),
            AnalysisRange: new SufniPlotAnalysisRangeTheme(
                SelectedFill: series.SuspensionFront.WithAlpha(inputs.PlotAnalysisSelectedFillOpacity),
                PreviewFill: inputs.PlotPreviewFill),
            Cursor: new SufniPlotCursorTheme(
                Line: inputs.PlotCursorLine,
                TooltipFill: surface.Page.WithAlpha(0.96),
                TooltipText: text.Emphasis,
                TooltipBorder: line.Strong),
            InPlotLabelText: text.High,
            ReferenceLine: inputs.PlotReferenceLine,
            Series: series);

        var palette = new SufniPalette(
            PageSurface: surface.Page,
            PlotDataArea: plot.Root.Data,
            ElevatedSurface: surface.Elevated,
            OverlayScrim: inputs.OverlayScrim,
            DialogSurface: surface.Page,
            PlaceholderPreviewSurface: inputs.PlaceholderPreviewSurface,
            FrontSeries: series.SuspensionFront,
            RearSeries: series.SuspensionRear,
            MarkerRed: inputs.MarkerRed,
            TravelZone: SufniThemes.TravelZoneRamp);

        return new SufniTheme(
            Mode: inputs.Mode,
            Palette: palette,
            Surface: surface,
            Text: text,
            Line: line,
            Action: action,
            Status: new SufniStatusTheme(
                Success: null,
                Warning: inputs.StatusWarning,
                Info: null),
            Selection: selection,
            Tab: tab,
            NavRail: navRail,
            List: list,
            SearchBar: searchBar,
            MapOverlay: mapOverlay,
            Splitter: splitter,
            Field: field,
            DragDrop: dragDrop,
            GraphRow: graphRow,
            Plot: plot,
            Typography: SufniThemes.Typography,
            Spacing: SufniThemes.Spacing);
    }
}
