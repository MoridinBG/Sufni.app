using System.Linq;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using Sufni.Telemetry;
using Sufni.App.Infrastructure.Theming;

namespace Sufni.App.Infrastructure;

/// <summary>The preferences snapshot used for the first frame and runtime initialization.</summary>
public sealed class LaunchPreferences
{
    private readonly UiLayoutProfile defaultLayoutProfile;

    public LaunchPreferences(UiLayoutProfile defaultLayoutProfile)
        : this(new AppPreferences.AppPreferencesDocument(), defaultLayoutProfile)
    {
    }

    internal LaunchPreferences(AppPreferences.AppPreferencesDocument document, UiLayoutProfile defaultLayoutProfile)
    {
        LegacyCustomLayers = (document.Maps?.CustomLayers ?? []).Where(config => config is not null).Select(config =>
        {
            var row = CustomTileLayer.From(config);
            row.Updated = document.LegacyUpdated ?? 0;
            return CustomTileLayerSnapshot.From(row);
        }).ToArray();
        var windows = new Dictionary<Guid, int>();
        foreach (var (id, preferences) in document.Session?.Sessions ?? [])
        {
            if (Guid.TryParse(id, out var sessionId) && preferences?.Processing?.VelocityFilterWindowMilliseconds is { } window)
                windows[sessionId] = new TelemetryProcessingOptions(window).ClampedVelocityFilterWindowMilliseconds;
        }
        LegacyProcessingWindows = windows.ToFrozenDictionary();
        Document = document.Normalize(AppPreferences.CurrentVersion);
        this.defaultLayoutProfile = defaultLayoutProfile;
    }

    // Runtime writes replace this document, preserving the launch snapshot.
    internal AppPreferences.AppPreferencesDocument Document { get; }
    internal IReadOnlyDictionary<Guid, int> LegacyProcessingWindows { get; }
    internal IReadOnlyList<CustomTileLayerSnapshot> LegacyCustomLayers { get; }
    public SufniThemeMode ThemeMode => Document.Theme.GetMode();
    public UiLayoutProfile LayoutProfile => Document.Ui.GetLayoutProfile() ?? defaultLayoutProfile;
}
