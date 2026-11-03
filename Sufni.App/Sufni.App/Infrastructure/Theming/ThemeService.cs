using Sufni.App.Shell.Services.Preferences;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Sufni.App.Infrastructure.Theming;

// Runtime theme coordinator for local preferences and Avalonia state.
public sealed class ThemeService : IThemeService
{
    private readonly IThemePreferences themePreferences;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    public ThemeService(IAppPreferences appPreferences, IUiThreadDispatcher uiThreadDispatcher)
    {
        themePreferences = appPreferences.Theme;
        this.uiThreadDispatcher = uiThreadDispatcher;
        IsSystemThemeAvailable = ResolveSystemThemeAvailable();
        Mode = NormalizeMode(ResolveModeFromApplication(IsSystemThemeAvailable));

    }

    public SufniThemeMode Mode { get; private set; }

    public SufniThemeMode EffectiveMode => ResolveEffectiveMode(Mode);

    public SufniTheme Current => SufniThemes.FromMode(EffectiveMode);

    public bool IsSystemThemeAvailable { get; private set; }

    public event EventHandler? ThemeChanged;

    public async Task InitializeAsync()
    {
        await ReconcileWithPersistedAsync();
    }

    private async Task ReconcileWithPersistedAsync()
    {
        RefreshSystemThemeAvailability();

        var persisted = NormalizeMode(await themePreferences.GetModeAsync());
        if (persisted == Mode && IsRequestedVariantApplied(persisted))
        {
            return;
        }

        await ApplyOnUiThreadAsync(persisted);
    }

    public Task ToggleAsync()
    {
        RefreshSystemThemeAvailability();

        var next = Mode switch
        {
            SufniThemeMode.Dark => SufniThemeMode.Light,
            SufniThemeMode.Light when IsSystemThemeAvailable => SufniThemeMode.System,
            SufniThemeMode.Light => SufniThemeMode.Dark,
            _ => SufniThemeMode.Dark
        };

        return SetAsync(next);
    }

    public async Task SetAsync(SufniThemeMode mode)
    {
        RefreshSystemThemeAvailability();
        mode = NormalizeMode(mode);
        if (mode == Mode && IsRequestedVariantApplied(mode))
        {
            return;
        }

        var previousMode = Mode;
        await ApplyOnUiThreadAsync(mode);
        if (mode != previousMode)
        {
            await themePreferences.SetModeAsync(mode);
        }
    }

    private async Task ApplyOnUiThreadAsync(SufniThemeMode mode)
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            Apply(mode);
            return;
        }

        await uiThreadDispatcher.InvokeAsync(() => Apply(mode));
    }

    private void Apply(SufniThemeMode mode)
    {
        mode = NormalizeMode(mode);
        Mode = mode;

        var app = Application.Current;
        app?.RequestedThemeVariant = SufniThemes.ToVariant(mode);

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private SufniThemeMode NormalizeMode(SufniThemeMode mode)
        => mode == SufniThemeMode.System && !IsSystemThemeAvailable
            ? SufniThemeMode.Dark
            : mode;

    private SufniThemeMode ResolveEffectiveMode(SufniThemeMode mode)
    {
        if (mode != SufniThemeMode.System)
        {
            return mode;
        }

        return SufniThemes.EffectiveModeFromVariant(Application.Current?.ActualThemeVariant);
    }

    private void RefreshSystemThemeAvailability()
    {
        IsSystemThemeAvailable = ResolveSystemThemeAvailable();
    }

    private static bool ResolveSystemThemeAvailable()
    {
        var platformSettings = Application.Current?.PlatformSettings;
        if (platformSettings is null)
        {
            return false;
        }

        var variant = platformSettings.GetColorValues().ThemeVariant;
        return variant == PlatformThemeVariant.Light || variant == PlatformThemeVariant.Dark;
    }

    private static bool IsRequestedVariantApplied(SufniThemeMode mode)
        => Application.Current?.RequestedThemeVariant == SufniThemes.ToVariant(mode);

    private static SufniThemeMode ResolveModeFromApplication(bool systemThemeAvailable)
    {
        var variant = Application.Current?.RequestedThemeVariant;
        return variant switch
        {
            { } value when value == ThemeVariant.Light => SufniThemeMode.Light,
            { } value when value == ThemeVariant.Default && systemThemeAvailable => SufniThemeMode.System,
            _ => SufniThemeMode.Dark
        };
    }
}
