using Avalonia.Controls;
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
}
