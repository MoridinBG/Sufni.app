using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Sufni.App.Services;

namespace Sufni.App.Theming;

// Runtime theme coordinator for preferences, sync updates, and Avalonia state.
public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly IThemePreferences themePreferences;
    private readonly IDisposable? syncSubscription;

    public ThemeService(IAppPreferences appPreferences)
    {
        themePreferences = appPreferences.Theme;
        Mode = ResolveModeFromApplication();

        // Inbound sync writes the new mode to the document but does not invoke
        // SetAsync, so without this we'd persist the new value yet keep
        // rendering the previous variant until the next restart.
        syncSubscription = appPreferences.SyncDataApplied.Subscribe(OnSyncDataApplied);
    }

    public SufniThemeMode Mode { get; private set; }

    public SufniTheme Current => SufniThemes.FromMode(Mode);

    public event EventHandler? ThemeChanged;

    public async Task InitializeAsync()
    {
        await ReconcileWithPersistedAsync();
    }

    private async Task ReconcileWithPersistedAsync()
    {
        var persisted = await themePreferences.GetModeAsync();
        if (persisted == Mode)
        {
            return;
        }

        await ApplyOnUiThreadAsync(persisted);
    }

    public void Dispose()
    {
        syncSubscription?.Dispose();
    }

    private void OnSyncDataApplied(System.Reactive.Unit unit)
    {
        _ = ReconcileWithPersistedAsync();
    }

    public Task ToggleAsync()
    {
        var next = Mode == SufniThemeMode.Light
            ? SufniThemeMode.Dark
            : SufniThemeMode.Light;
        return SetAsync(next);
    }

    public async Task SetAsync(SufniThemeMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        await ApplyOnUiThreadAsync(mode);
        await themePreferences.SetModeAsync(mode);
    }

    private async Task ApplyOnUiThreadAsync(SufniThemeMode mode)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply(mode);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => Apply(mode));
    }

    private void Apply(SufniThemeMode mode)
    {
        Mode = mode;

        var app = Application.Current;
        if (app is not null)
        {
            app.RequestedThemeVariant = SufniThemes.ToVariant(mode);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SufniThemeMode ResolveModeFromApplication()
    {
        var variant = Application.Current?.RequestedThemeVariant;
        return variant == ThemeVariant.Light
            ? SufniThemeMode.Light
            : SufniThemeMode.Dark;
    }
}
