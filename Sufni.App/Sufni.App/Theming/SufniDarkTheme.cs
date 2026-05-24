using static Sufni.App.Theming.SufniDarkPalette;

namespace Sufni.App.Theming;

// Concrete token set for the dark app theme.
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
        var action = new SufniActionTheme(
            AccentPrimary: AccentBlue,
            AccentPrimaryHover: AccentBlueLight,
            Danger: DangerRed,
            DangerDark: DangerRedDark,
            AccentAliases: new SufniAccentAliasTheme(
                TabUnderline: AccentBlue,
                NavPipe: AccentBlue,
                DropPosition: AccentBlue,
                FocusRing: AccentBlue,
                Hyperlink: AccentBlue,
                Spinner: AccentBlue),
            Disabled: new SufniDisabledActionTheme(surface.InputDisabled, text.Disabled, 0.3));
        var selection = new SufniSelectionTheme(
            SurfaceSubtle: SlateMidnight,
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
            BorderDisabled: Coal,
            Height: 39);
        var dragDrop = new SufniDragDropTheme(
            Header: DragHeader,
            FeedbackOpacity: 0.72,
            DropTargetHeader: DropTargetTeal,
            DropPositionIndicator: action.AccentAliases.DropPosition);
        var graphRow = new SufniGraphRowTheme(
            Root: new SufniGraphRowDepthTheme(
                Container: surface.Page,
                Header: surface.Elevated,
                PlotFigure: surface.Page,
                PlotData: surface.Input),
            HostedLevel1: new SufniGraphRowDepthTheme(
                Container: TarContainer,
                Header: TarHeader,
                PlotFigure: TarFigure,
                PlotData: TarData),
            HostedLevel2: new SufniGraphRowDepthTheme(
                Container: PitchContainer,
                Header: PitchHeader,
                PlotFigure: PitchFigure,
                PlotData: PitchData),
            HostedLevel3Plus: new SufniGraphRowDepthTheme(
                Container: VoidContainer,
                Header: VoidHeader,
                PlotFigure: VoidFigure,
                PlotData: VoidData),
            Connector: SlateGray,
            DividerBetweenRoots: line.Divider);
        var travelZone = SufniThemes.TravelZoneRamp;
        var series = SufniThemes.SignalSeries;
        var plot = new SufniPlotTheme(
            Root: new SufniPlotDepthTheme(Obsidian, SlateAbyss),
            HostedLevel1: new SufniPlotDepthTheme(TarFigure, TarData),
            HostedLevel2: new SufniPlotDepthTheme(PitchFigure, PitchData),
            HostedLevel3Plus: new SufniPlotDepthTheme(VoidFigure, VoidData),
            Grid: new SufniPlotGridTheme(SteelGray, Gunmetal),
            Axis: new SufniPlotAxisTheme(SteelGray, text.Primary, text.Primary),
            Legend: new SufniPlotLegendTheme(surface.Elevated, line.Subtle, text.Primary),
            Marker: new SufniPlotMarkerTheme(
                Line: MarkerBlue.WithAlpha(0.9),
                AirtimeFill: MarkerBlue.WithAlpha(0.2),
                AirtimeOutline: text.Secondary.WithAlpha(0.5)),
            AnalysisRange: new SufniPlotAnalysisRangeTheme(
                SelectedFill: series.SuspensionFront.WithAlpha(0.16),
                PreviewFill: SlateMist.WithAlpha(0.12)),
            Cursor: new SufniPlotCursorTheme(
                Line: Pearl,
                TooltipFill: surface.Page.WithAlpha(0.96),
                TooltipText: text.Emphasis,
                TooltipBorder: line.Strong),
            InPlotLabelText: text.High,
            ReferenceLine: Mist,
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
            MarkerRed: SufniDarkPalette.MarkerRed,
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
                Warning: WarningGold,
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
