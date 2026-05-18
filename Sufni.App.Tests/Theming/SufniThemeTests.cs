using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Sufni.App.Theming;

namespace Sufni.App.Tests.Theming;

public class SufniThemeTests
{
    [Fact]
    public void SufniDarkTheme_GraphRowByDepth_UsesRootHostedLevelsAndClamp()
    {
        var graphRow = SufniDarkTheme.Instance.GraphRow;

        Assert.Equal(graphRow.Root, graphRow.ByDepth(0));
        Assert.Equal(graphRow.Root, graphRow.ByDepth(-1));
        Assert.Equal(graphRow.HostedLevel1, graphRow.ByDepth(1));
        Assert.Equal(graphRow.HostedLevel2, graphRow.ByDepth(2));
        Assert.Equal(graphRow.HostedLevel3Plus, graphRow.ByDepth(3));
        Assert.Equal(graphRow.HostedLevel3Plus, graphRow.ByDepth(10));
    }

    [Fact]
    public void SufniThemes_SignalSeries_IsVariantInvariant()
    {
        Assert.Equal(SufniThemes.SignalSeries, SufniThemes.Dark.Plot.Series);
        Assert.Equal(SufniThemes.SignalSeries, SufniThemes.Light.Plot.Series);
        Assert.Equal(SufniThemes.TravelZoneRamp, SufniThemes.SignalSeries.TravelZone);
    }

    [Fact]
    public void SufniLightTheme_PlotDataBackgrounds_AreDarkerThanInputSurface()
    {
        var theme = SufniLightTheme.Instance;

        Assert.Equal(Color.Parse("#E8EDF2"), theme.GraphRow.Root.PlotData);
        Assert.Equal(theme.GraphRow.Root.PlotData, theme.Plot.Root.Data);
        Assert.Equal(theme.Plot.Root.Data, theme.Palette.PlotDataArea);
    }

    [Fact]
    public void SufniThemes_ToVariant_MapsSystemModeToAvaloniaDefault()
    {
        Assert.Equal(ThemeVariant.Dark, SufniThemes.ToVariant(SufniThemeMode.Dark));
        Assert.Equal(ThemeVariant.Light, SufniThemes.ToVariant(SufniThemeMode.Light));
        Assert.Equal(ThemeVariant.Default, SufniThemes.ToVariant(SufniThemeMode.System));
    }

    [Fact]
    public void SufniThemes_EffectiveModeFromVariant_UsesDarkForUnknownOrDefault()
    {
        Assert.Equal(SufniThemeMode.Light, SufniThemes.EffectiveModeFromVariant(ThemeVariant.Light));
        Assert.Equal(SufniThemeMode.Dark, SufniThemes.EffectiveModeFromVariant(ThemeVariant.Dark));
        Assert.Equal(SufniThemeMode.Dark, SufniThemes.EffectiveModeFromVariant(ThemeVariant.Default));
        Assert.Equal(SufniThemeMode.Dark, SufniThemes.EffectiveModeFromVariant(null));
    }

    [Fact]
    public void SufniThemeResourceDictionary_LegacyResources_AreDerivedFromDarkTheme()
    {
        var theme = SufniDarkTheme.Instance;
        var resources = new SufniThemeResourceDictionary();
        var dark = ResolveVariant(resources, ThemeVariant.Dark);

        Assert.Equal(theme.Text.Secondary, dark["SufniForeground"]);
        Assert.Equal(theme.Surface.Page, dark["SufniRegion"]);
        Assert.Equal(theme.Surface.ItemHover, dark["SufniItemBackgroundPointerOver"]);
        Assert.Equal(theme.Action.AccentPrimary, dark["SufniAccentColor"]);
        Assert.Equal(theme.Action.AccentAliases.Hyperlink, dark["SufniAccentHyperlink"]);
        Assert.Equal(theme.Action.AccentAliases.Spinner, dark["SufniAccentSpinner"]);
        AssertSolidBrush(theme.Field.SurfaceFocused, dark["TextControlBackgroundFocused"]);
        AssertSolidBrush(theme.Field.BorderDisabled, dark["TextControlBorderBrushDisabled"]);
    }

    [Fact]
    public void SufniThemeResourceDictionary_LegacyResources_AreDerivedFromLightTheme()
    {
        var theme = SufniLightTheme.Instance;
        var resources = new SufniThemeResourceDictionary();
        var light = ResolveVariant(resources, ThemeVariant.Light);

        Assert.Equal(theme.Text.Secondary, light["SufniForeground"]);
        Assert.Equal(theme.Surface.Page, light["SufniRegion"]);
        Assert.Equal(theme.Action.AccentPrimary, light["SufniAccentColor"]);
        AssertSolidBrush(theme.Field.SurfaceFocused, light["TextControlBackgroundFocused"]);
        AssertSolidBrush(theme.Field.BorderDisabled, light["TextControlBorderBrushDisabled"]);
    }

    [Fact]
    public void SufniThemeResourceDictionary_RootResources_AreVariantInvariant()
    {
        var resources = new SufniThemeResourceDictionary();

        // Spacing / typography / dimension keys live at the root and must be
        // resolvable regardless of variant.
        Assert.True(resources.ContainsKey("SufniSpacingControlHeight"));
        Assert.True(resources.ContainsKey("SufniSearchBarHeight"));
        Assert.True(resources.ContainsKey("SufniMapOverlayOpacity"));
        Assert.True(resources.ContainsKey("SufniSelectionIndicatorThickness"));
        Assert.True(resources.ContainsKey("SufniNavRailPaneCompactWidth"));
        Assert.True(resources.ContainsKey("SufniTabIndicatorBleed"));
        Assert.True(resources.ContainsKey("SufniFontSizeBody"));
        Assert.True(resources.ContainsKey("SufniTypographyBodyFontSize"));
    }

    [Fact]
    public void SufniThemeResourceDictionary_SelectionAndDisabledRoles_UseSeparateResourceKeys()
    {
        var theme = SufniDarkTheme.Instance;
        var resources = new SufniThemeResourceDictionary();
        var dark = ResolveVariant(resources, ThemeVariant.Dark);

        Assert.True(dark.TryGetValue("SufniSelectionSurfaceSubtle", out var selectionSurface));
        Assert.True(dark.TryGetValue("SufniSurfaceInputDisabled", out var disabledSurface));
        Assert.Equal(theme.Selection.SurfaceSubtle, selectionSurface);
        Assert.Equal(theme.Surface.InputDisabled, disabledSurface);
        Assert.Equal(selectionSurface, disabledSurface);
    }

    [Fact]
    public void SufniThemeResourceDictionary_PlotAnalysisRangeResources_AreDerivedFromTheme()
    {
        var resources = new SufniThemeResourceDictionary();
        var dark = ResolveVariant(resources, ThemeVariant.Dark);
        var light = ResolveVariant(resources, ThemeVariant.Light);

        Assert.Equal(SufniDarkTheme.Instance.Plot.AnalysisRange.SelectedFill, dark["SufniPlotAnalysisRangeSelectedFill"]);
        AssertSolidBrush(SufniDarkTheme.Instance.Plot.AnalysisRange.PreviewFill, dark["SufniPlotAnalysisRangePreviewFillBrush"]);
        Assert.Equal(SufniLightTheme.Instance.Plot.AnalysisRange.SelectedFill, light["SufniPlotAnalysisRangeSelectedFill"]);
        AssertSolidBrush(SufniLightTheme.Instance.Plot.AnalysisRange.PreviewFill, light["SufniPlotAnalysisRangePreviewFillBrush"]);
    }

    [Fact]
    public void SufniThemeResourceDictionary_FluentOverrides_ExistInBothVariantDictionaries()
    {
        var resources = new SufniThemeResourceDictionary();
        var dark = ResolveVariant(resources, ThemeVariant.Dark);
        var light = ResolveVariant(resources, ThemeVariant.Light);

        // These keys override FluentTheme defaults. If they are missing from
        // either variant child dictionary, the other variant's brush leaks
        // through and the controls render with the wrong palette.
        foreach (var key in new[]
                 {
                     "ButtonBackground",
                     "ButtonForeground",
                     "ButtonBackgroundPointerOver",
                     "TextControlBackground",
                     "TextControlForeground",
                     "TextControlBorderBrush",
                     "TextControlBackgroundFocused",
                     "ComboBoxBackground",
                     "ComboBoxForeground",
                     "ComboBoxDropDownBackground",
                     "ExpanderHeaderBackground",
                     "FlyoutPresenterBackground",
                 })
        {
            Assert.True(dark.ContainsKey(key), $"Dark variant missing {key}");
            Assert.True(light.ContainsKey(key), $"Light variant missing {key}");
        }
    }

    private static ResourceDictionary ResolveVariant(SufniThemeResourceDictionary resources, ThemeVariant variant)
    {
        Assert.True(resources.ThemeDictionaries.TryGetValue(variant, out var provider));
        return Assert.IsType<ResourceDictionary>(provider);
    }

    private static void AssertSolidBrush(Color expectedColor, object? resource)
    {
        var brush = Assert.IsType<SolidColorBrush>(resource);
        Assert.Equal(expectedColor, brush.Color);
    }
}
