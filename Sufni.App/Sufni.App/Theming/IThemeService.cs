using System;
using System.Threading.Tasks;

namespace Sufni.App.Theming;

// Owns the persisted theme mode and applies it to the running Avalonia app.
public interface IThemeService
{
    SufniThemeMode Mode { get; }
    SufniThemeMode EffectiveMode { get; }
    SufniTheme Current { get; }
    bool IsSystemThemeAvailable { get; }

    event EventHandler? ThemeChanged;

    // Reconciles startup state with the persisted preference.
    Task InitializeAsync();

    Task ToggleAsync();

    // Applies and persists an explicit theme mode.
    Task SetAsync(SufniThemeMode mode);
}
