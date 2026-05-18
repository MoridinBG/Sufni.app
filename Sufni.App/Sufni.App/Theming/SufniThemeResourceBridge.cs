using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sufni.App.Theming;

// Converts typed theme tokens into Avalonia resource keys used by XAML.
public static class SufniThemeResourceBridge
{
    // Populates variant-invariant spacing, typography, and dimension resources.
    public static void PopulateRoot(ResourceDictionary resources)
    {
        // Root resources are variant-invariant numeric tokens. Both themes
        // currently share these values, so the fallback theme is the metadata
        // source for spacing, typography, dimensions, and opacities.
        var theme = SufniThemes.Fallback;

        resources["SufniActionDisabledIconOpacity"] = theme.Action.Disabled.IconOpacity;

        resources["SufniSelectionIndicatorThickness"] = theme.Selection.IndicatorThickness;
        resources["SufniSelectionIndicatorLengthVertical"] = theme.Selection.IndicatorLengthVertical;

        resources["SufniTabIndicatorBleed"] = theme.Tab.IndicatorBleed;
        resources["SufniTabIndicatorBleedMargin"] = new Thickness(-theme.Tab.IndicatorBleed, 0, -theme.Tab.IndicatorBleed, 0);
        resources["SufniTabFontSizeWindow"] = theme.Tab.WindowFontSize;
        resources["SufniTabFontSizeStatistics"] = theme.Tab.StatisticsFontSize;
        resources["SufniTabFontSizeNav"] = theme.Tab.NavFontSize;

        resources["SufniNavRailPaneCompactWidth"] = theme.NavRail.CompactPaneWidth;

        resources["SufniSearchBarCornerRadius"] = new CornerRadius(theme.SearchBar.CornerRadius);
        resources["SufniSearchBarBorderThickness"] = new Thickness(theme.SearchBar.BorderThickness);
        resources["SufniSearchBarHeight"] = theme.SearchBar.Height;
        resources["SufniSearchBarHeightGridLength"] = new GridLength(theme.SearchBar.Height);

        resources["SufniMapOverlayOpacity"] = theme.MapOverlay.Opacity;
        resources["SufniMapOverlayCornerRadius"] = new CornerRadius(theme.MapOverlay.CornerRadius);
        resources["SufniMapOverlayPadding"] = new Thickness(theme.MapOverlay.Padding);

        resources["SufniSplitterMinThickness"] = theme.Splitter.MinThickness;

        resources["SufniFieldHeight"] = theme.Field.Height;

        resources["SufniDragFeedbackOpacity"] = theme.DragDrop.FeedbackOpacity;

        resources["SufniFontFamily"] = string.IsNullOrWhiteSpace(theme.Typography.FontFamilyName)
            ? FontFamily.Default
            : new FontFamily(theme.Typography.FontFamilyName);
        AddTypographyRole(resources, "SufniTypographyBody", theme.Typography.Body);
        AddTypographyRole(resources, "SufniTypographyCompactLabel", theme.Typography.CompactLabel);
        AddTypographyRole(resources, "SufniTypographyRowHeader", theme.Typography.RowHeader);
        AddTypographyRole(resources, "SufniTypographyAxisLabel", theme.Typography.AxisLabel);
        AddTypographyRole(resources, "SufniTypographyAxisTick", theme.Typography.AxisTick);
        AddTypographyRole(resources, "SufniTypographyLegend", theme.Typography.Legend);
        AddTypographyRole(resources, "SufniTypographyReadoutHeader", theme.Typography.ReadoutHeader);
        AddTypographyRole(resources, "SufniTypographyReadoutLine", theme.Typography.ReadoutLine);
        AddTypographyRole(resources, "SufniTypographyInPlotLabel", theme.Typography.InPlotLabel);
        AddTypographyRole(resources, "SufniTypographyTab", theme.Typography.Tab);
        AddTypographyRole(resources, "SufniTypographyAction", theme.Typography.Action);
        AddTypographyRole(resources, "SufniTypographyPlaceholder", theme.Typography.Placeholder);
        AddTypographyRole(resources, "SufniTypographyFieldText", theme.Typography.FieldText);

        resources["SufniFontSizeCaption"] = theme.Typography.Size.Caption;
        resources["SufniFontSizeSmall"] = theme.Typography.Size.Small;
        resources["SufniFontSizeBody"] = theme.Typography.Size.Body;
        resources["SufniFontSizeLabel"] = theme.Typography.Size.Label;
        resources["SufniFontSizeHeading"] = theme.Typography.Size.Heading;
        resources["SufniFontSizeDisplay"] = theme.Typography.Size.Display;

        resources["SufniSpacingHierarchyIndent"] = theme.Spacing.HierarchyIndent;
        resources["SufniSpacingHeaderHorizontalPadding"] = theme.Spacing.HeaderHorizontalPadding;
        resources["SufniSpacingHeaderGlyphWidth"] = theme.Spacing.HeaderGlyphWidth;
        resources["SufniSpacingConnectorLineWidth"] = theme.Spacing.ConnectorLineWidth;
        resources["SufniSpacingConnectorStemInsetFromGlyphLeft"] = theme.Spacing.ConnectorStemInsetFromGlyphLeft;
        resources["SufniSpacingConnectorGlyphGap"] = theme.Spacing.ConnectorGlyphGap;
        resources["SufniSpacingControlHeight"] = theme.Spacing.ControlHeight;
        resources["SufniSpacingControlHeightGridLength"] = new GridLength(theme.Spacing.ControlHeight);
        resources["SufniSpacingBaseRowDividerHeight"] = theme.Spacing.BaseRowDividerHeight;
        resources["SufniSpacingRootDropZoneHeight"] = theme.Spacing.RootDropZoneHeight;

        resources["FlyoutContentThemePadding"] = new Thickness(0);
        resources["FlyoutThemeMaxWidth"] = 600d;
        resources["OverlayCornerRadius"] = new CornerRadius(3);
        resources["FlyoutBorderThemeThickness"] = new Thickness(0);
    }

    // Populates color and brush resources for a single Avalonia theme variant.
    public static void PopulateVariant(ResourceDictionary resources, SufniTheme theme)
    {
        AddLegacyColors(resources, theme);
        AddFluentOverrides(resources, theme);
        AddRoleColors(resources, theme);
    }

    private static void AddLegacyColors(ResourceDictionary resources, SufniTheme theme)
    {
        SetColor(resources, "SufniForeground", theme.Text.Secondary);
        SetColor(resources, "SufniForegroundPointerOver", theme.Text.Hover);
        SetColor(resources, "SufniRegion", theme.Surface.Page);
        SetColor(resources, "SufniBackground", theme.Surface.Input);
        SetColor(resources, "SufniForegroundDisabled", theme.Text.Disabled);
        SetColor(resources, "SufniBackgroundDisabled", theme.Surface.InputDisabled);
        SetColor(resources, "SufniBackgroundPointerOver", theme.Surface.InputHover);
        SetColor(resources, "SufniItemBackgroundPointerOver", theme.Surface.ItemHover);
        SetColor(resources, "SufniBorderBrush", theme.Line.Input);
        SetColor(resources, "SufniDangerColor", theme.Action.Danger);
        SetColor(resources, "SufniDangerColorDark", theme.Action.DangerDark);
        SetColor(resources, "SufniAccentColor", theme.Action.AccentPrimary);
        SetColor(resources, "SufniGridSplitter", theme.Splitter.Surface);
    }

    private static void AddFluentOverrides(ResourceDictionary resources, SufniTheme theme)
    {
        SetBrush(resources, "ExpanderHeaderBackground", theme.Surface.Input);
        SetBrush(resources, "ExpanderHeaderBackgroundPressed", theme.Surface.Input);
        SetBrush(resources, "ExpanderHeaderBackgroundPointerOver", theme.Surface.Input);

        SetBrush(resources, "ButtonBackground", theme.Surface.Input);
        SetBrush(resources, "ButtonForeground", theme.Text.Secondary);
        SetBrush(resources, "ButtonBackgroundDisabled", theme.Action.Disabled.Surface);
        SetBrush(resources, "ButtonForegroundDisabled", theme.Action.Disabled.Text);
        SetBrush(resources, "ButtonBackgroundPointerOver", theme.Surface.InputHover);
        SetBrush(resources, "ButtonForegroundPointerOver", theme.Text.Secondary);
        SetBrush(resources, "ButtonBackgroundPressed", theme.Surface.InputPressed);
        SetBrush(resources, "ButtonForegroundPressed", theme.Text.Secondary);

        SetBrush(resources, "TextControlForeground", theme.Field.Value);
        SetBrush(resources, "TextControlBackground", theme.Field.Surface);
        SetBrush(resources, "TextControlBorderBrush", theme.Field.Border);
        SetBrush(resources, "TextControlForegroundDisabled", theme.Action.Disabled.Text);
        SetBrush(resources, "TextControlBackgroundDisabled", theme.Action.Disabled.Surface);
        SetBrush(resources, "TextControlBorderBrushDisabled", theme.Field.BorderDisabled);
        SetBrush(resources, "TextControlForegroundPointerOver", theme.Field.Value);
        SetBrush(resources, "TextControlBackgroundPointerOver", theme.Field.SurfaceFocused);
        SetBrush(resources, "TextControlBorderBrushPointerOver", theme.Field.Border);
        SetBrush(resources, "TextControlForegroundFocused", theme.Field.Value);
        SetBrush(resources, "TextControlBackgroundFocused", theme.Field.SurfaceFocused);
        SetBrush(resources, "TextControlBorderBrushFocused", theme.Field.BorderFocused);

        SetBrush(resources, "ComboBoxForeground", theme.Field.Value);
        SetBrush(resources, "ComboBoxBackground", theme.Field.Surface);
        SetBrush(resources, "ComboBoxBorderBrush", theme.Field.Border);
        SetBrush(resources, "ComboBoxBackgroundPointerOver", theme.Surface.InputHover);
        SetBrush(resources, "ComboBoxBorderBrushPointerOver", theme.Field.Border);
        SetBrush(resources, "ComboBoxBackgroundPressed", theme.Surface.InputPressed);
        SetBrush(resources, "ComboBoxBorderBrushPressed", theme.Field.Border);
        SetBrush(resources, "ComboBoxDropDownBackground", theme.Field.Surface);

        SetBrush(resources, "CalendarDatePickerForeground", theme.Field.Value);
        SetBrush(resources, "CalendarDatePickerBorderBrush", theme.Field.Border);
        SetBrush(resources, "CalendarDatePickerBorderBrushPointerOver", theme.Field.Border);

        SetBrush(resources, "FlyoutPresenterBackground", theme.Surface.Input);
    }

    private static void AddRoleColors(ResourceDictionary resources, SufniTheme theme)
    {
        AddColorPair(resources, "SufniSurfacePage", theme.Surface.Page);
        AddColorPair(resources, "SufniSurfaceElevated", theme.Surface.Elevated);
        AddColorPair(resources, "SufniSurfaceInput", theme.Surface.Input);
        AddColorPair(resources, "SufniSurfaceInputHover", theme.Surface.InputHover);
        AddColorPair(resources, "SufniSurfaceInputPressed", theme.Surface.InputPressed);
        AddColorPair(resources, "SufniSurfaceInputDisabled", theme.Surface.InputDisabled);
        AddColorPair(resources, "SufniSurfaceItemHover", theme.Surface.ItemHover);
        AddColorPair(resources, "SufniSurfaceInputFocused", theme.Surface.InputFocused);

        AddColorPair(resources, "SufniTextHigh", theme.Text.High);
        AddColorPair(resources, "SufniTextEmphasis", theme.Text.Emphasis);
        AddColorPair(resources, "SufniTextPrimary", theme.Text.Primary);
        AddColorPair(resources, "SufniTextSecondary", theme.Text.Secondary);
        AddColorPair(resources, "SufniTextHover", theme.Text.Hover);
        AddColorPair(resources, "SufniTextDisabled", theme.Text.Disabled);

        AddColorPair(resources, "SufniLineSubtle", theme.Line.Subtle);
        AddColorPair(resources, "SufniLineDefault", theme.Line.Default);
        AddColorPair(resources, "SufniLineDivider", theme.Line.Divider);
        AddColorPair(resources, "SufniLineInput", theme.Line.Input);
        AddColorPair(resources, "SufniLineStrong", theme.Line.Strong);
        AddColorPair(resources, "SufniLineGridMinor", theme.Line.GridMinor);

        AddColorPair(resources, "SufniAccentPrimary", theme.Action.AccentPrimary);
        AddColorPair(resources, "SufniAccentPrimaryHover", theme.Action.AccentPrimaryHover);
        AddColorPair(resources, "SufniAccentIndicatorTabUnderline", theme.Action.AccentAliases.TabUnderline);
        AddColorPair(resources, "SufniAccentIndicatorNavPipe", theme.Action.AccentAliases.NavPipe);
        AddColorPair(resources, "SufniAccentIndicatorDropPosition", theme.Action.AccentAliases.DropPosition);
        AddColorPair(resources, "SufniAccentFocusRing", theme.Action.AccentAliases.FocusRing);
        AddColorPair(resources, "SufniAccentHyperlink", theme.Action.AccentAliases.Hyperlink);
        AddColorPair(resources, "SufniAccentSpinner", theme.Action.AccentAliases.Spinner);
        AddColorPair(resources, "SufniDanger", theme.Action.Danger);
        AddColorPair(resources, "SufniDangerDark", theme.Action.DangerDark);
        AddOptionalColorPair(resources, "SufniStatusSuccess", theme.Status.Success);
        AddOptionalColorPair(resources, "SufniStatusWarning", theme.Status.Warning);
        AddOptionalColorPair(resources, "SufniStatusInfo", theme.Status.Info);
        AddColorPair(resources, "SufniActionDisabledSurface", theme.Action.Disabled.Surface);
        AddColorPair(resources, "SufniActionDisabledText", theme.Action.Disabled.Text);

        AddColorPair(resources, "SufniSelectionSurfaceSubtle", theme.Selection.SurfaceSubtle);
        AddColorPair(resources, "SufniSelectionIndicator", theme.Selection.Indicator);

        AddColorPair(resources, "SufniTabText", theme.Tab.Text);
        AddColorPair(resources, "SufniTabTextSelected", theme.Tab.TextSelected);
        AddColorPair(resources, "SufniTabTextHover", theme.Tab.TextHover);
        AddColorPair(resources, "SufniTabSurfaceHover", theme.Tab.SurfaceHover);
        AddColorPair(resources, "SufniTabSurfaceSelected", theme.Tab.SurfaceSelected);
        AddColorPair(resources, "SufniTabIndicator", theme.Tab.Indicator);

        AddColorPair(resources, "SufniNavRailBackground", theme.NavRail.Background);
        AddColorPair(resources, "SufniNavRailBackgroundBottom", theme.NavRail.BottomBackground);
        AddColorPair(resources, "SufniNavRailBorderRight", theme.NavRail.BorderRight);

        AddColorPair(resources, "SufniListRowSurface", theme.List.RowSurface);
        AddColorPair(resources, "SufniListRowSurfaceHover", theme.List.RowSurfaceHover);
        AddColorPair(resources, "SufniListRowDivider", theme.List.RowDivider);

        AddColorPair(resources, "SufniSearchBarSurface", theme.SearchBar.Surface);

        AddColorPair(resources, "SufniMapOverlaySurface", theme.MapOverlay.Surface);
        AddColorPair(resources, "SufniMapOverlayText", theme.MapOverlay.Text);

        AddColorPair(resources, "SufniSplitterSurface", theme.Splitter.Surface);

        AddColorPair(resources, "SufniFieldLabel", theme.Field.Label);
        AddColorPair(resources, "SufniFieldValue", theme.Field.Value);
        AddColorPair(resources, "SufniFieldSurface", theme.Field.Surface);
        AddColorPair(resources, "SufniFieldSurfaceFocused", theme.Field.SurfaceFocused);
        AddColorPair(resources, "SufniFieldBorder", theme.Field.Border);
        AddColorPair(resources, "SufniFieldBorderFocused", theme.Field.BorderFocused);
        AddColorPair(resources, "SufniFieldBorderDisabled", theme.Field.BorderDisabled);

        AddColorPair(resources, "SufniGraphRowConnector", theme.GraphRow.Connector);
        AddColorPair(resources, "SufniGraphRowDividerBetweenRoots", theme.GraphRow.DividerBetweenRoots);
        AddGraphRowDepth(resources, "SufniGraphRowRoot", theme.GraphRow.Root);
        AddGraphRowDepth(resources, "SufniGraphRowHostedLevel1", theme.GraphRow.HostedLevel1);
        AddGraphRowDepth(resources, "SufniGraphRowHostedLevel2", theme.GraphRow.HostedLevel2);
        AddGraphRowDepth(resources, "SufniGraphRowHostedLevel3Plus", theme.GraphRow.HostedLevel3Plus);
        AddColorPair(resources, "SufniDragFeedbackHeader", theme.DragDrop.Header);
        AddColorPair(resources, "SufniDropTargetHeader", theme.DragDrop.DropTargetHeader);
        AddColorPair(resources, "SufniDropPositionIndicator", theme.DragDrop.DropPositionIndicator);

        AddPlotDepth(resources, "SufniPlotRoot", theme.Plot.Root);
        AddPlotDepth(resources, "SufniPlotHostedLevel1", theme.Plot.HostedLevel1);
        AddPlotDepth(resources, "SufniPlotHostedLevel2", theme.Plot.HostedLevel2);
        AddPlotDepth(resources, "SufniPlotHostedLevel3Plus", theme.Plot.HostedLevel3Plus);
        AddColorPair(resources, "SufniPlotGridMajor", theme.Plot.Grid.Major);
        AddColorPair(resources, "SufniPlotGridMinor", theme.Plot.Grid.Minor);
        AddColorPair(resources, "SufniPlotAxisLine", theme.Plot.Axis.Line);
        AddColorPair(resources, "SufniPlotAxisLabel", theme.Plot.Axis.Label);
        AddColorPair(resources, "SufniPlotAxisTick", theme.Plot.Axis.Tick);
        AddColorPair(resources, "SufniPlotLegendBackground", theme.Plot.Legend.Background);
        AddColorPair(resources, "SufniPlotLegendBorder", theme.Plot.Legend.Border);
        AddColorPair(resources, "SufniPlotLegendText", theme.Plot.Legend.Text);
        AddColorPair(resources, "SufniPlotMarkerLine", theme.Plot.Marker.Line);
        AddColorPair(resources, "SufniPlotAnalysisRangeSelectedFill", theme.Plot.AnalysisRange.SelectedFill);
        AddColorPair(resources, "SufniPlotAnalysisRangePreviewFill", theme.Plot.AnalysisRange.PreviewFill);
        AddColorPair(resources, "SufniPlotCursorLine", theme.Plot.Cursor.Line);
        AddColorPair(resources, "SufniPlotCursorTooltipFill", theme.Plot.Cursor.TooltipFill);
        AddColorPair(resources, "SufniPlotCursorTooltipText", theme.Plot.Cursor.TooltipText);
        AddColorPair(resources, "SufniPlotCursorTooltipBorder", theme.Plot.Cursor.TooltipBorder);
        AddColorPair(resources, "SufniPlotInLabelText", theme.Plot.InPlotLabelText);

        AddColorPair(resources, "SufniSeriesSuspensionFront", theme.Plot.Series.SuspensionFront);
        AddColorPair(resources, "SufniSeriesSuspensionRear", theme.Plot.Series.SuspensionRear);
        AddColorPair(resources, "SufniSeriesImuFrame", theme.Plot.Series.ImuFrame);
        AddColorPair(resources, "SufniSeriesImuFork", theme.Plot.Series.ImuFork);
        AddColorPair(resources, "SufniSeriesImuShock", theme.Plot.Series.ImuShock);
        AddColorPair(resources, "SufniSeriesGpsSpeed", theme.Plot.Series.GpsSpeed);
        AddColorPair(resources, "SufniSeriesGpsElevation", theme.Plot.Series.GpsElevation);
    }

    private static void AddGraphRowDepth(ResourceDictionary resources, string prefix, SufniGraphRowDepthTheme depth)
    {
        AddColorPair(resources, $"{prefix}Container", depth.Container);
        AddColorPair(resources, $"{prefix}Header", depth.Header);
        AddColorPair(resources, $"{prefix}PlotFigure", depth.PlotFigure);
        AddColorPair(resources, $"{prefix}PlotData", depth.PlotData);
    }

    private static void AddPlotDepth(ResourceDictionary resources, string prefix, SufniPlotDepthTheme depth)
    {
        AddColorPair(resources, $"{prefix}Figure", depth.Figure);
        AddColorPair(resources, $"{prefix}Data", depth.Data);
    }

    private static void AddTypographyRole(ResourceDictionary resources, string prefix, SufniTypographyRole role)
    {
        resources[$"{prefix}FontSize"] = role.Size;
        resources[$"{prefix}FontWeight"] = ToFontWeight(role.Weight);
    }

    private static FontWeight ToFontWeight(SufniThemeFontWeight weight)
        => weight switch
        {
            SufniThemeFontWeight.Regular => FontWeight.Regular,
            SufniThemeFontWeight.Medium => FontWeight.Medium,
            SufniThemeFontWeight.SemiBold => FontWeight.SemiBold,
            _ => FontWeight.Regular
        };

    private static void AddColorPair(ResourceDictionary resources, string key, Color color)
    {
        SetColor(resources, key, color);
        SetBrush(resources, $"{key}Brush", color);
    }

    private static void AddOptionalColorPair(ResourceDictionary resources, string key, Color? color)
    {
        if (color is { } value)
        {
            AddColorPair(resources, key, value);
        }
    }

    private static void SetColor(ResourceDictionary resources, string key, Color color)
        => resources[key] = color;

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
        => resources[key] = color.ToBrush();
}
