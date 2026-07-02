using Avalonia.Controls;
using Avalonia.Styling;
using Sufni.App.Theming;

using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Tests.Theming;

public class SufniThemeTests
{
    [Fact]
    public void SufniDarkTheme_SignalRowByDepth_UsesRootHostedLevelsAndClamp()
    {
        var signalRow = SufniDarkTheme.Instance.SignalRow;

        Assert.Equal(signalRow.Root, signalRow.ByDepth(0));
        Assert.Equal(signalRow.Root, signalRow.ByDepth(-1));
        Assert.Equal(signalRow.HostedLevel1, signalRow.ByDepth(1));
        Assert.Equal(signalRow.HostedLevel2, signalRow.ByDepth(2));
        Assert.Equal(signalRow.HostedLevel3Plus, signalRow.ByDepth(3));
        Assert.Equal(signalRow.HostedLevel3Plus, signalRow.ByDepth(10));
    }

    [Fact]
    public void SufniThemes_TypographyAndSpacing_AreSharedAcrossVariants()
    {
        // Typography and spacing are variant-invariant; both themes must draw
        // from the single shared source so they cannot silently diverge (and so
        // PopulateRoot's variant-invariant treatment stays correct).
        Assert.Same(SufniThemes.Typography, SufniThemes.Dark.Typography);
        Assert.Same(SufniThemes.Typography, SufniThemes.Light.Typography);
        Assert.Same(SufniThemes.Spacing, SufniThemes.Dark.Spacing);
        Assert.Same(SufniThemes.Spacing, SufniThemes.Light.Spacing);
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

    [Fact]
    public void SufniThemeResourceDictionary_AppBrushes_ExistInBothVariantDictionaries()
    {
        var resources = new SufniThemeResourceDictionary();
        var dark = ResolveVariant(resources, ThemeVariant.Dark);
        var light = ResolveVariant(resources, ThemeVariant.Light);

        foreach (var key in new[]
                 {
                     "SufniOverlayScrimBrush",
                     "SufniDialogSurfaceBrush",
                     "SufniSurfacePlaceholderPreviewBrush",
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
