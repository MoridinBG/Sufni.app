using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.Infrastructure;

public enum UiLayoutProfile
{
    Compact,
    Workspace,
}

public sealed record AppCapabilities(
    bool CanHostSyncServer,
    bool CanPairAsClient,
    bool HasHaptics,
    bool SupportsMassStorageImport,
    bool SupportsStorageProviderImport,
    bool SupportsNativeWindowing);

public sealed record InputCapabilities(
    bool HasPointer,
    bool HasTouch,
    bool HasKeyboard,
    bool SupportsPinch,
    bool SupportsLongPressContextMenu);

public interface IAppEnvironment
{
    UiLayoutProfile DefaultLayoutProfile { get; }
    UiLayoutProfile LayoutProfile { get; }
    AppCapabilities Capabilities { get; }
    InputCapabilities Input { get; }
}

public sealed record AppEnvironment(
    UiLayoutProfile DefaultLayoutProfile,
    UiLayoutProfile LayoutProfile,
    AppCapabilities Capabilities,
    InputCapabilities Input) : IAppEnvironment;

public static class AppEnvironmentRegistration
{
    public static IServiceCollection AddAppEnvironment(
        this IServiceCollection services,
        UiLayoutProfile defaultLayoutProfile,
        AppCapabilities capabilities,
        InputCapabilities input)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(input);

        return services.AddSingleton<IAppEnvironment>(_ =>
            new AppEnvironment(
                defaultLayoutProfile,
                UiLayoutProfileBootstrap.ReadPersistedProfile(defaultLayoutProfile),
                capabilities,
                input));
    }
}

public static class UiLayoutProfileBootstrap
{
    public static UiLayoutProfile ReadPersistedProfile(UiLayoutProfile fallback)
    {
        try
        {
            var directory = Path.GetDirectoryName(AppPaths.DatabasePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return fallback;
            }

            var filePath = Path.Combine(directory, "app-preferences.json");
            if (!File.Exists(filePath))
            {
                return fallback;
            }

            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("ui", out var uiElement)
                || uiElement.ValueKind != JsonValueKind.Object
                || !uiElement.TryGetProperty("layoutProfile", out var layoutProfileElement)
                || layoutProfileElement.ValueKind != JsonValueKind.String)
            {
                return fallback;
            }

            return Enum.TryParse<UiLayoutProfile>(layoutProfileElement.GetString(), ignoreCase: false, out var parsed)
                ? parsed
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
