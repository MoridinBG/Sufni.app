using System.Collections.Generic;
using Avalonia.Media;

namespace Sufni.App.Theming;

// Immutable root object for every app, graph, and plot theme token.
public sealed record SufniTheme(
    SufniThemeMode Mode,
    SufniPalette Palette,
    SufniSurfaceTheme Surface,
    SufniTextTheme Text,
    SufniLineTheme Line,
    SufniActionTheme Action,
    SufniStatusTheme Status,
    SufniSelectionTheme Selection,
    SufniTabTheme Tab,
    SufniNavRailTheme NavRail,
    SufniListTheme List,
    SufniSearchBarTheme SearchBar,
    SufniMapOverlayTheme MapOverlay,
    SufniSplitterTheme Splitter,
    SufniFieldTheme Field,
    SufniDragDropTheme DragDrop,
    SufniGraphRowTheme GraphRow,
    SufniPlotTheme Plot,
    SufniTypographyTheme Typography,
    SufniSpacingTheme Spacing);

public sealed record SufniPalette(
    Color PageSurface,
    Color PlotDataArea,
    Color ElevatedSurface,
    Color FrontSeries,
    Color RearSeries,
    Color MarkerRed,
    IReadOnlyList<Color> TravelZone);

public sealed record SufniSurfaceTheme(
    Color Page,
    Color Elevated,
    Color Input,
    Color InputHover,
    Color InputPressed,
    Color InputDisabled,
    Color ItemHover,
    Color InputFocused);

public sealed record SufniTextTheme(
    Color High,
    Color Emphasis,
    Color Primary,
    Color Secondary,
    Color Hover,
    Color Disabled);

public sealed record SufniLineTheme(
    Color Subtle,
    Color Default,
    Color Divider,
    Color Input,
    Color Strong,
    Color GridMinor);

public sealed record SufniActionTheme(
    Color AccentPrimary,
    Color AccentPrimaryHover,
    Color Danger,
    Color DangerDark,
    SufniAccentAliasTheme AccentAliases,
    SufniDisabledActionTheme Disabled);

public sealed record SufniAccentAliasTheme(
    Color TabUnderline,
    Color NavPipe,
    Color DropPosition,
    Color FocusRing,
    Color Hyperlink,
    Color Spinner);

public sealed record SufniDisabledActionTheme(
    Color Surface,
    Color Text,
    double IconOpacity);

public sealed record SufniStatusTheme(
    Color? Success,
    Color? Warning,
    Color? Info);

public sealed record SufniSelectionTheme(
    Color SurfaceSubtle,
    Color Indicator,
    double IndicatorThickness,
    double IndicatorLengthVertical);

public sealed record SufniTabTheme(
    Color Text,
    Color TextSelected,
    Color TextHover,
    Color SurfaceHover,
    Color SurfaceSelected,
    Color Indicator,
    double IndicatorBleed,
    double WindowFontSize,
    double StatisticsFontSize,
    double NavFontSize);

public sealed record SufniNavRailTheme(
    double CompactPaneWidth,
    Color Background,
    Color BottomBackground,
    Color BorderRight);

public sealed record SufniListTheme(
    Color RowSurface,
    Color RowSurfaceHover,
    Color RowDivider);

public sealed record SufniSearchBarTheme(
    Color Surface,
    double CornerRadius,
    double BorderThickness,
    double Height);

public sealed record SufniMapOverlayTheme(
    Color Surface,
    double Opacity,
    double CornerRadius,
    double Padding,
    Color Text);

public sealed record SufniSplitterTheme(
    Color Surface,
    double MinThickness);

public sealed record SufniFieldTheme(
    Color Label,
    Color Value,
    Color Surface,
    Color SurfaceFocused,
    Color Border,
    Color BorderFocused,
    Color BorderDisabled,
    double Height);

public sealed record SufniDragDropTheme(
    Color Header,
    double FeedbackOpacity,
    Color DropTargetHeader,
    Color DropPositionIndicator);

// Per-depth graph row chrome used by recorded and hosted telemetry rows.
public sealed record SufniGraphRowTheme(
    SufniGraphRowDepthTheme Root,
    SufniGraphRowDepthTheme HostedLevel1,
    SufniGraphRowDepthTheme HostedLevel2,
    SufniGraphRowDepthTheme HostedLevel3Plus,
    Color Connector,
    Color DividerBetweenRoots)
{
    // Clamps arbitrary nesting levels into the deepest defined row style.
    public SufniGraphRowDepthTheme ByDepth(int depth)
        => depth switch
        {
            <= 0 => Root,
            1 => HostedLevel1,
            2 => HostedLevel2,
            _ => HostedLevel3Plus
        };
}

public sealed record SufniGraphRowDepthTheme(
    Color Container,
    Color Header,
    Color PlotFigure,
    Color PlotData);

// Plot theme tokens shared by ScottPlot models and plot-hosting controls.
public sealed record SufniPlotTheme(
    SufniPlotDepthTheme Root,
    SufniPlotDepthTheme HostedLevel1,
    SufniPlotDepthTheme HostedLevel2,
    SufniPlotDepthTheme HostedLevel3Plus,
    SufniPlotGridTheme Grid,
    SufniPlotAxisTheme Axis,
    SufniPlotLegendTheme Legend,
    SufniPlotMarkerTheme Marker,
    SufniPlotAnalysisRangeTheme AnalysisRange,
    SufniPlotCursorTheme Cursor,
    Color InPlotLabelText,
    Color ReferenceLine,
    SufniSeriesTheme Series)
{
    // Clamps arbitrary nesting levels into the deepest defined plot style.
    public SufniPlotDepthTheme ByDepth(int depth)
        => depth switch
        {
            <= 0 => Root,
            1 => HostedLevel1,
            2 => HostedLevel2,
            _ => HostedLevel3Plus
        };
}

public sealed record SufniPlotDepthTheme(
    Color Figure,
    Color Data);

public sealed record SufniPlotGridTheme(
    Color Major,
    Color Minor);

public sealed record SufniPlotAxisTheme(
    Color Line,
    Color Label,
    Color Tick);

public sealed record SufniPlotLegendTheme(
    Color Background,
    Color Border,
    Color Text);

public sealed record SufniPlotMarkerTheme(
    Color Line,
    Color AirtimeFill,
    Color AirtimeOutline);

public sealed record SufniPlotAnalysisRangeTheme(
    Color SelectedFill,
    Color PreviewFill);

public sealed record SufniPlotCursorTheme(
    Color Line,
    Color TooltipFill,
    Color TooltipText,
    Color TooltipBorder);

// Theme-invariant colors for telemetry series with domain meaning.
public sealed record SufniSeriesTheme(
    Color SuspensionFront,
    Color SuspensionRear,
    Color ImuFrame,
    Color ImuFork,
    Color ImuShock,
    Color GpsSpeed,
    Color GpsElevation,
    Color GpsQuality,
    IReadOnlyList<Color> TravelZone);

public sealed record SufniTypographyTheme(
    string FontFamilyName,
    SufniFontSizeTheme Size,
    SufniTypographyRole Body,
    SufniTypographyRole CompactLabel,
    SufniTypographyRole RowHeader,
    SufniTypographyRole AxisLabel,
    SufniTypographyRole AxisTick,
    SufniTypographyRole Legend,
    SufniTypographyRole ReadoutHeader,
    SufniTypographyRole ReadoutLine,
    SufniTypographyRole InPlotLabel,
    SufniTypographyRole Tab,
    SufniTypographyRole Action,
    SufniTypographyRole Placeholder,
    SufniTypographyRole FieldText);

public sealed record SufniFontSizeTheme(
    double Caption,
    double Small,
    double Body,
    double Label,
    double Heading,
    double Display);

public sealed record SufniTypographyRole(
    double Size,
    SufniThemeFontWeight Weight);

public enum SufniThemeFontWeight
{
    Regular,
    Medium,
    SemiBold
}

public sealed record SufniSpacingTheme(
    double HierarchyIndent,
    double HeaderHorizontalPadding,
    double HeaderGlyphWidth,
    double ConnectorLineWidth,
    double ConnectorStemInsetFromGlyphLeft,
    double ConnectorGlyphGap,
    double ControlHeight,
    double BaseRowDividerHeight,
    double RootDropZoneHeight);
