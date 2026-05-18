using Avalonia.Controls;
using Avalonia.Styling;

namespace Sufni.App.Theming;

// Resource dictionary that exposes Sufni dark and light themes to Avalonia.
public sealed class SufniThemeResourceDictionary : ResourceDictionary
{
    public SufniThemeResourceDictionary()
    {
        SufniThemeResourceBridge.PopulateRoot(this);

        var darkResources = new ResourceDictionary();
        SufniThemeResourceBridge.PopulateVariant(darkResources, SufniThemes.Dark);
        ThemeDictionaries[ThemeVariant.Dark] = darkResources;

        var lightResources = new ResourceDictionary();
        SufniThemeResourceBridge.PopulateVariant(lightResources, SufniThemes.Light);
        ThemeDictionaries[ThemeVariant.Light] = lightResources;
    }
}
