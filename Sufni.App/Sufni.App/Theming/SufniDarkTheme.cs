using Avalonia.Media;

namespace Sufni.App.Theming;

// Concrete token set for the dark app theme.
public static class SufniDarkTheme
{
    public static SufniTheme Instance { get; } = Create();

    private static SufniTheme Create()
    {
        static Color C(string hex) => Color.Parse(hex);

        var surface = new SufniSurfaceTheme(
            Page: C("#15191C"),
            Elevated: C("#1A1F23"),
            Input: C("#20262B"),
            InputHover: C("#2C3032"),
            InputPressed: C("#2C3032"),
            InputDisabled: C("#25292C"),
            ItemHover: C("#272D33"),
            InputFocused: C("#10161B"));
        var text = new SufniTextTheme(
            High: C("#FEFEFE"),
            Emphasis: C("#F0F0F0"),
            Primary: C("#D0D0D0"),
            Secondary: C("#A0A0A0"),
            Hover: C("#C0C0C0"),
            Disabled: C("#606060"));
        var line = new SufniLineTheme(
            Subtle: C("#343C42"),
            Default: C("#505558"),
            Divider: C("#404040"),
            Input: C("#505050"),
            Strong: C("#5A5A5A"),
            GridMinor: C("#3A3F42"));
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
            SurfaceSubtle: C("#25292C"),
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
            Surface: surface.Input,
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
            BorderDisabled: C("#303030"),
            Height: 39);
        var dragDrop = new SufniDragDropTheme(
            Header: C("#263238"),
            FeedbackOpacity: 0.72,
            DropTargetHeader: C("#1F3A46"),
            DropPositionIndicator: action.AccentAliases.DropPosition);
        var graphRow = new SufniGraphRowTheme(
            Root: new SufniGraphRowDepthTheme(
                Container: surface.Page,
                Header: surface.Elevated,
                PlotFigure: surface.Page,
                PlotData: surface.Input),
            HostedLevel1: new SufniGraphRowDepthTheme(
                Container: C("#0F1314"),
                Header: C("#101416"),
                PlotFigure: C("#101518"),
                PlotData: C("#1B2126")),
            HostedLevel2: new SufniGraphRowDepthTheme(
                Container: C("#0A0C0D"),
                Header: C("#07090A"),
                PlotFigure: C("#0A0D0F"),
                PlotData: C("#11161A")),
            HostedLevel3Plus: new SufniGraphRowDepthTheme(
                Container: C("#050607"),
                Header: C("#030404"),
                PlotFigure: C("#050708"),
                PlotData: C("#0B0F12")),
            Connector: C("#63727A"),
            DividerBetweenRoots: line.Divider);
        var travelZone = SufniThemes.TravelZoneRamp;
        var series = SufniThemes.SignalSeries;
        var plot = new SufniPlotTheme(
            Root: new SufniPlotDepthTheme(C("#15191C"), C("#20262B")),
            HostedLevel1: new SufniPlotDepthTheme(C("#101518"), C("#1B2126")),
            HostedLevel2: new SufniPlotDepthTheme(C("#0A0D0F"), C("#11161A")),
            HostedLevel3Plus: new SufniPlotDepthTheme(C("#050708"), C("#0B0F12")),
            Grid: new SufniPlotGridTheme(C("#505558"), C("#3A3F42")),
            Axis: new SufniPlotAxisTheme(C("#505558"), text.Primary, text.Primary),
            Legend: new SufniPlotLegendTheme(surface.Elevated, line.Subtle, text.Primary),
            Marker: new SufniPlotMarkerTheme(
                Line: C("#D53E4F").WithAlpha(0.9),
                AirtimeFill: C("#D53E4F").WithAlpha(0.2),
                AirtimeOutline: text.Secondary.WithAlpha(0.5)),
            Cursor: new SufniPlotCursorTheme(
                Line: C("#C8C8C8"),
                TooltipFill: surface.Page.WithAlpha(0.96),
                TooltipText: text.Emphasis,
                TooltipBorder: line.Strong),
            InPlotLabelText: text.High,
            ReferenceLine: C("#DDDDDD"),
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
            Mode: SufniThemeMode.Dark,
            Palette: palette,
            Surface: surface,
            Text: text,
            Line: line,
            Action: action,
            Status: new SufniStatusTheme(
                Success: null,
                Warning: C("#DAA520"),
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
