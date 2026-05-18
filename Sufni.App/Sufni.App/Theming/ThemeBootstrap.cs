using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Sufni.App.Services;

namespace Sufni.App.Theming;

// One-shot synchronous read of the persisted theme mode, run from
// App.Initialize() before XAML loads. Without this, the first frame renders
// the dark variant declared in App.axaml and then snaps to the persisted
// variant once IThemeService.InitializeAsync completes, causing a visible flash.
public static class ThemeBootstrap
{
    public static void ApplyPersistedVariant(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var mode = ReadPersistedMode();
        application.RequestedThemeVariant = SufniThemes.ToVariant(mode);
    }

    private static SufniThemeMode ReadPersistedMode()
    {
        try
        {
            var directory = Path.GetDirectoryName(AppPaths.DatabasePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return SufniThemeMode.Dark;
            }

            var filePath = Path.Combine(directory, "app-preferences.json");
            if (!File.Exists(filePath))
            {
                return SufniThemeMode.Dark;
            }

            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("theme", out var themeElement)
                || themeElement.ValueKind != JsonValueKind.Object
                || !themeElement.TryGetProperty("mode", out var modeElement)
                || modeElement.ValueKind != JsonValueKind.String)
            {
                return SufniThemeMode.Dark;
            }

            return Enum.TryParse<SufniThemeMode>(modeElement.GetString(), ignoreCase: false, out var parsed)
                ? parsed
                : SufniThemeMode.Dark;
        }
        catch
        {
            return SufniThemeMode.Dark;
        }
    }
}
