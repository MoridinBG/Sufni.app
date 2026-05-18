using Avalonia.Media;

namespace Sufni.App.Theming;

// Concrete token set for the light app theme.
public static class SufniLightTheme
{
    public static SufniTheme Instance { get; } = Create();

    private static SufniTheme Create()
    {
        static Color C(string hex) => Color.Parse(hex);

        var surface = new SufniSurfaceTheme(
            Page: C("#E4E8EC"),
            Elevated: C("#EDEFF2"),
            Input: C("#F2F4F6"),
            InputHover: C("#E0E4E8"),
            InputPressed: C("#D6DADF"),
            InputDisabled: C("#D8DCE0"),
            ItemHover: C("#DDE1E5"),
            InputFocused: C("#F2F4F6"));
        var text = new SufniTextTheme(
            High: C("#0A0E12"),
            Emphasis: C("#1A1F24"),
            Primary: C("#22272D"),
            Secondary: C("#5A6068"),
            Hover: C("#3A3F45"),
            Disabled: C("#9AA0A6"));
        var line = new SufniLineTheme(
            Subtle: C("#D5DAE0"),
            Default: C("#B0B5BA"),
            Divider: C("#CFD3D7"),
            Input: C("#C8CCD0"),
            Strong: C("#9CA0A4"),
            GridMinor: C("#E0E4E8"));
        var action = new SufniActionTheme(
            AccentPrimary: C("#0078D7"),
            AccentPrimaryHover: C("#1E8AE0"),
            Danger: C("#BF312D"),
            DangerDark: C("#9F110D"),
            AccentAliases: new SufniAccentAliasTheme(
                TabUnderline: C("#0078D7"),
                NavPipe: C("#0078D7"),
                DropPosition: C("#0078D7"),
                FocusRing: C("#0078D7"),
                Hyperlink: C("#0078D7"),
                Spinner: C("#0078D7")),
            Disabled: new SufniDisabledActionTheme(surface.InputDisabled, text.Disabled, 0.3));
        var selection = new SufniSelectionTheme(
            SurfaceSubtle: C("#DCE6F2"),
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
            Surface: surface.Elevated,
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
            BorderDisabled: C("#E0E4E8"),
            Height: 39);
        var dragDrop = new SufniDragDropTheme(
            Header: C("#D8E5EC"),
            FeedbackOpacity: 0.72,
            DropTargetHeader: C("#C8DBEA"),
            DropPositionIndicator: action.AccentAliases.DropPosition);
        var graphRow = new SufniGraphRowTheme(
            Root: new SufniGraphRowDepthTheme(
                Container: surface.Page,
                Header: surface.Elevated,
                PlotFigure: surface.Page,
                PlotData: surface.Input),
            HostedLevel1: new SufniGraphRowDepthTheme(
                Container: C("#D8DCE0"),
                Header: C("#D4D8DC"),
                PlotFigure: C("#DCE0E4"),
                PlotData: C("#E4E8EC")),
            HostedLevel2: new SufniGraphRowDepthTheme(
                Container: C("#CCD0D4"),
                Header: C("#C8CCD0"),
                PlotFigure: C("#D0D4D8"),
                PlotData: C("#DCE0E4")),
            HostedLevel3Plus: new SufniGraphRowDepthTheme(
                Container: C("#C0C4C8"),
                Header: C("#BCC0C4"),
                PlotFigure: C("#C4C8CC"),
                PlotData: C("#D2D6DA")),
            Connector: C("#9CA0A4"),
            DividerBetweenRoots: line.Divider);
        var travelZone = SufniThemes.TravelZoneRamp;
        var series = SufniThemes.SignalSeries;
        var plot = new SufniPlotTheme(
            Root: new SufniPlotDepthTheme(C("#E4E8EC"), C("#F2F4F6")),
            HostedLevel1: new SufniPlotDepthTheme(C("#DCE0E4"), C("#E4E8EC")),
            HostedLevel2: new SufniPlotDepthTheme(C("#D0D4D8"), C("#DCE0E4")),
            HostedLevel3Plus: new SufniPlotDepthTheme(C("#C4C8CC"), C("#D2D6DA")),
            Grid: new SufniPlotGridTheme(C("#B0B5BA"), C("#D6DADF")),
            Axis: new SufniPlotAxisTheme(C("#B0B5BA"), text.Primary, text.Primary),
            Legend: new SufniPlotLegendTheme(surface.Elevated, line.Subtle, text.Primary),
            Marker: new SufniPlotMarkerTheme(
                Line: C("#D53E4F").WithAlpha(0.9),
                AirtimeFill: C("#D53E4F").WithAlpha(0.2),
                AirtimeOutline: text.Secondary.WithAlpha(0.5)),
            Cursor: new SufniPlotCursorTheme(
                Line: text.Secondary,
                TooltipFill: surface.Page.WithAlpha(0.96),
                TooltipText: text.Emphasis,
                TooltipBorder: line.Strong),
            InPlotLabelText: text.High,
            ReferenceLine: C("#9CA0A4"),
            Series: series);
        var typography = new SufniTypographyTheme(
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
        var spacing = new SufniSpacingTheme(
            HierarchyIndent: 16,
            HeaderHorizontalPadding: 8,
            HeaderGlyphWidth: 20,
            ConnectorLineWidth: 2,
            ConnectorStemInsetFromGlyphLeft: 2,
            ConnectorGlyphGap: 6,
            ControlHeight: 39,
            BaseRowDividerHeight: 6,
            RootDropZoneHeight: 12);
        var palette = new SufniPalette(
            PageSurface: surface.Page,
            PlotDataArea: surface.Input,
            ElevatedSurface: surface.Elevated,
            FrontSeries: series.SuspensionFront,
            RearSeries: series.SuspensionRear,
            MarkerRed: C("#D53E4F"),
            TravelZone: travelZone);

        return new SufniTheme(
            Mode: SufniThemeMode.Light,
            Palette: palette,
            Surface: surface,
            Text: text,
            Line: line,
            Action: action,
            Status: new SufniStatusTheme(
                Success: null,
                Warning: C("#B7861A"),
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
            Typography: typography,
            Spacing: spacing);
    }
}
