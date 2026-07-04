using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Sufni.App.Theming;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Tests.Infrastructure;

public class AppPreferencesTests
{
    [Fact]
    public async Task MapPreferences_PersistSelectedLayerAndCustomLayers_UnderMapsGroup()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var layer = new TileLayerConfig
        {
            Id = Guid.NewGuid(),
            Name = "Trail maps",
            UrlTemplate = "https://tiles.example/{z}/{x}/{y}.png",
            AttributionText = "Example tiles",
            AttributionUrl = "https://tiles.example",
            MaxZoom = 18,
            IsCustom = true,
        };

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Map.SetSelectedLayerIdAsync(layer.Id);
        await preferences.Map.SetCustomLayersAsync([layer]);

        var reloaded = new AppPreferences(preferencesPath);
        var selectedLayerId = await reloaded.Map.GetSelectedLayerIdAsync();
        var customLayers = await reloaded.Map.GetCustomLayersAsync();

        Assert.Equal(layer.Id, selectedLayerId);
        var customLayer = Assert.Single(customLayers);
        Assert.Equal(layer.Id, customLayer.Id);
        Assert.Equal("Trail maps", customLayer.Name);

        customLayer.Name = "Mutated outside preferences";
        var rereadCustomLayer = Assert.Single(await reloaded.Map.GetCustomLayersAsync());
        Assert.Equal("Trail maps", rereadCustomLayer.Name);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var maps = json.RootElement.GetProperty("maps");
        Assert.Equal(layer.Id.ToString("D"), maps.GetProperty("selectedLayerId").GetString());
        Assert.Single(maps.GetProperty("customLayers").EnumerateArray());
        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*.tmp"));
    }

    [Fact]
    public async Task Preferences_ReadsFromLoadedDocument_WhenFileChangesAfterConstruction()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var initialLayerId = Guid.NewGuid();
        var externalLayerId = Guid.NewGuid();

        await File.WriteAllTextAsync(
            preferencesPath,
            $$"""
            {
              "version": 1,
              "maps": {
                "selectedLayerId": "{{initialLayerId:D}}",
                "customLayers": []
              }
            }
            """);
        var preferences = new AppPreferences(preferencesPath);

        await File.WriteAllTextAsync(
            preferencesPath,
            $$"""
            {
              "version": 1,
              "maps": {
                "selectedLayerId": "{{externalLayerId:D}}",
                "customLayers": []
              }
            }
            """);

        Assert.Equal(initialLayerId, await preferences.Map.GetSelectedLayerIdAsync());

        var reloaded = new AppPreferences(preferencesPath);
        Assert.Equal(externalLayerId, await reloaded.Map.GetSelectedLayerIdAsync());
    }

    [Fact]
    public async Task MapPreferences_ObserveChanges_ReplaysAndReportsLocalAndSyncOrigins()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var localLayerId = Guid.NewGuid();
        var syncedLayer = new TileLayerConfig
        {
            Id = Guid.NewGuid(),
            Name = "Synced custom",
            UrlTemplate = "https://tiles.example/{z}/{x}/{y}.png",
            AttributionText = "Example",
            AttributionUrl = "https://tiles.example",
            MaxZoom = 18,
            IsCustom = true,
        };
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<PreferenceValueChange<MapPreferencesValue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var localEmission = new TaskCompletionSource<PreferenceValueChange<MapPreferencesValue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncEmission = new TaskCompletionSource<PreferenceValueChange<MapPreferencesValue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Map.ObserveChanges()
            .Subscribe(change =>
            {
                var emission = Interlocked.Increment(ref emissions);
                if (emission == 1)
                {
                    initialEmission.TrySetResult(change);
                }
                else if (emission == 2)
                {
                    localEmission.TrySetResult(change);
                }
                else
                {
                    syncEmission.TrySetResult(change);
                }
            });

        var initial = await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, initial.Origin);
        Assert.False(initial.AdvancesSyncClock);
        Assert.Null(initial.Value.SelectedLayerId);
        Assert.Empty(initial.Value.CustomLayers);

        await preferences.Map.SetSelectedLayerIdAsync(localLayerId);

        var local = await localEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.LocalWrite, local.Origin);
        Assert.True(local.AdvancesSyncClock);
        Assert.Equal(localLayerId, local.Value.SelectedLayerId);

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 9_999_999_999,
            Maps = new MapPreferencesSyncData
            {
                SelectedLayerId = syncedLayer.Id,
                CustomLayers = [syncedLayer],
            },
        });

        var synced = await syncEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, synced.Origin);
        Assert.False(synced.AdvancesSyncClock);
        Assert.Equal(syncedLayer.Id, synced.Value.SelectedLayerId);
        Assert.Equal(syncedLayer.Id, Assert.Single(synced.Value.CustomLayers).Id);
    }

    [Fact]
    public async Task ThemePreferences_DefaultMode_IsDark_WhenFileIsMissing()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");

        var preferences = new AppPreferences(preferencesPath);

        Assert.Equal(SufniThemeMode.Dark, await preferences.Theme.GetModeAsync());
    }

    [Fact]
    public async Task ThemePreferences_PersistMode_RoundTripsThroughDiskAndJson()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");

        var preferences = new AppPreferences(preferencesPath);
        await preferences.Theme.SetModeAsync(SufniThemeMode.Light);

        var reloaded = new AppPreferences(preferencesPath);
        Assert.Equal(SufniThemeMode.Light, await reloaded.Theme.GetModeAsync());

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        Assert.Equal("Light", json.RootElement.GetProperty("theme").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task ThemePreferences_ObserveModeChanges_ReplaysAndReportsLocalAndSyncOrigins()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<PreferenceValueChange<SufniThemeMode>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var localEmission = new TaskCompletionSource<PreferenceValueChange<SufniThemeMode>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncEmission = new TaskCompletionSource<PreferenceValueChange<SufniThemeMode>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Theme.ObserveModeChanges()
            .Subscribe(change =>
            {
                var emission = Interlocked.Increment(ref emissions);
                if (emission == 1)
                {
                    initialEmission.TrySetResult(change);
                }
                else if (emission == 2)
                {
                    localEmission.TrySetResult(change);
                }
                else
                {
                    syncEmission.TrySetResult(change);
                }
            });

        var initial = await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, initial.Origin);
        Assert.False(initial.AdvancesSyncClock);
        Assert.Equal(SufniThemeMode.Dark, initial.Value);

        await preferences.Theme.SetModeAsync(SufniThemeMode.Light);

        var local = await localEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.LocalWrite, local.Origin);
        Assert.True(local.AdvancesSyncClock);
        Assert.Equal(SufniThemeMode.Light, local.Value);

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 9_999_999_999,
            Theme = new ThemePreferencesSyncData { Mode = SufniThemeMode.Dark.ToString() },
        });

        var synced = await syncEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, synced.Origin);
        Assert.False(synced.AdvancesSyncClock);
        Assert.Equal(SufniThemeMode.Dark, synced.Value);
    }

    [Fact]
    public async Task ThemePreferences_PersistSystemMode_RoundTripsThroughDiskAndJson()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");

        var preferences = new AppPreferences(preferencesPath);
        await preferences.Theme.SetModeAsync(SufniThemeMode.System);

        var reloaded = new AppPreferences(preferencesPath);
        Assert.Equal(SufniThemeMode.System, await reloaded.Theme.GetModeAsync());

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        Assert.Equal("System", json.RootElement.GetProperty("theme").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task ThemePreferences_FallsBackToDark_WhenStoredModeIsUnknown()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");

        await File.WriteAllTextAsync(preferencesPath, "{\"version\":1,\"theme\":{\"mode\":\"Solarized\"}}");
        var preferences = new AppPreferences(preferencesPath);

        Assert.Equal(SufniThemeMode.Dark, await preferences.Theme.GetModeAsync());
    }

    [Fact]
    public async Task UiPreferences_ObserveChanges_ReplaysAndReportsNoClockLocalWrites()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<PreferenceValueChange<UiPreferences>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var localEmission = new TaskCompletionSource<PreferenceValueChange<UiPreferences>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Ui.ObserveChanges()
            .Subscribe(change =>
            {
                if (Interlocked.Increment(ref emissions) == 1)
                {
                    initialEmission.TrySetResult(change);
                }
                else
                {
                    localEmission.TrySetResult(change);
                }
            });

        var initial = await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, initial.Origin);
        Assert.False(initial.AdvancesSyncClock);
        Assert.Null(initial.Value.LayoutProfile);

        await preferences.Ui.SetLayoutProfileAsync(UiLayoutProfile.Workspace);

        var local = await localEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.LocalNoSyncClockWrite, local.Origin);
        Assert.False(local.AdvancesSyncClock);
        Assert.Equal(UiLayoutProfile.Workspace, local.Value.LayoutProfile);
        Assert.Null(await preferences.GetSyncDataAsync(0));
    }

    [Fact]
    public async Task SessionPreferences_ReturnDefaults_WhenFileOrSessionIsMissing()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");

        var preferences = new AppPreferences(preferencesPath);

        var missingFilePreferences = await preferences.Session.GetRecordedAsync(Guid.NewGuid());
        AssertDefaultSessionPreferences(missingFilePreferences);

        await File.WriteAllTextAsync(preferencesPath, "{\"version\":1}");
        var missingSessionPreferences = await preferences.Session.GetRecordedAsync(Guid.NewGuid());

        AssertDefaultSessionPreferences(missingSessionPreferences);
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsAllSignalDisplayVisibilityValues()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalDisplay = current.SignalDisplay with
            {
                Travel = false,
                Velocity = true,
                Imu = false,
                Speed = true,
                Elevation = false,
            },
        });

        var reloaded = new AppPreferences(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        Assert.False(stored.SignalDisplay.Travel);
        Assert.True(stored.SignalDisplay.Velocity);
        Assert.False(stored.SignalDisplay.Imu);
        Assert.True(stored.SignalDisplay.Speed);
        Assert.False(stored.SignalDisplay.Elevation);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var session = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"));
        AssertSignalDisplayValues(session.GetProperty("signalDisplay"));
        Assert.False(session.TryGetProperty("plots", out _));

        static void AssertSignalDisplayValues(JsonElement signalDisplay)
        {
            Assert.False(signalDisplay.GetProperty("travel").GetBoolean());
            Assert.True(signalDisplay.GetProperty("velocity").GetBoolean());
            Assert.False(signalDisplay.GetProperty("imu").GetBoolean());
            Assert.True(signalDisplay.GetProperty("speed").GetBoolean());
            Assert.False(signalDisplay.GetProperty("elevation").GetBoolean());
        }
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_MergesSessionEntryWithoutDeletingOthers()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(firstSessionId, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Imu = false },
        });
        await preferences.Session.UpdateRecordedAsync(secondSessionId, current => current with
        {
            Analysis = current.Analysis with
            {
                TravelDistributionMode = TravelDistributionMode.DynamicSag,
                SessionInsightsTargetProfile = SessionInsightsTargetProfile.Enduro,
            },
        });

        var first = await preferences.Session.GetRecordedAsync(firstSessionId);
        var second = await preferences.Session.GetRecordedAsync(secondSessionId);

        Assert.False(first.SignalDisplay.Imu);
        Assert.Equal(TravelDistributionMode.ActiveSuspension, first.Analysis.TravelDistributionMode);
        Assert.True(second.SignalDisplay.Imu);
        Assert.Equal(TravelDistributionMode.DynamicSag, second.Analysis.TravelDistributionMode);
        Assert.Equal(SessionInsightsTargetProfile.Enduro, second.Analysis.SessionInsightsTargetProfile);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var sessions = json.RootElement.GetProperty("session").GetProperty("sessions");
        Assert.Equal(2, sessions.EnumerateObject().Count());
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsSignalSmoothingLevels()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalDisplay = current.SignalDisplay with
            {
                TravelSmoothing = PlotSmoothingLevel.Light,
                VelocitySmoothing = PlotSmoothingLevel.Strong,
                ImuSmoothing = PlotSmoothingLevel.Off,
                SpeedSmoothing = PlotSmoothingLevel.Light,
                ElevationSmoothing = PlotSmoothingLevel.Strong,
            },
        });

        var reloaded = new AppPreferences(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        Assert.Equal(PlotSmoothingLevel.Light, stored.SignalDisplay.TravelSmoothing);
        Assert.Equal(PlotSmoothingLevel.Strong, stored.SignalDisplay.VelocitySmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.SignalDisplay.ImuSmoothing);
        Assert.Equal(PlotSmoothingLevel.Light, stored.SignalDisplay.SpeedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Strong, stored.SignalDisplay.ElevationSmoothing);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var session = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"));
        AssertSmoothingValues(session.GetProperty("signalDisplay"));
        Assert.False(session.TryGetProperty("plots", out _));

        static void AssertSmoothingValues(JsonElement signalDisplay)
        {
            Assert.Equal("Light", signalDisplay.GetProperty("travelSmoothing").GetString());
            Assert.Equal("Strong", signalDisplay.GetProperty("velocitySmoothing").GetString());
            Assert.Equal("Off", signalDisplay.GetProperty("imuSmoothing").GetString());
            Assert.Equal("Light", signalDisplay.GetProperty("speedSmoothing").GetString());
            Assert.Equal("Strong", signalDisplay.GetProperty("elevationSmoothing").GetString());
        }
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsProcessingPreference()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            Processing = new SessionProcessingPreferences(VelocityFilterWindowMilliseconds: 250),
        });

        var reloaded = new AppPreferences(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        Assert.Equal(250, stored.Processing.VelocityFilterWindowMilliseconds);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var processing = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"))
            .GetProperty("processing");
        Assert.Equal(250, processing.GetProperty("velocityFilterWindowMilliseconds").GetInt32());
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsSignalLayoutHierarchyAndExpansion()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var signalLayout = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(
                SignalRowIds.Imu,
                isExpanded: false,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.Velocity),
                ]),
            new SignalLayoutRowPreferences(
                SignalRowIds.Travel,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.Speed, isExpanded: false),
                ]),
        ]);

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalLayout = signalLayout,
        });

        var reloaded = new AppPreferences(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        Assert.Equal(signalLayout, stored.SignalLayout);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var session = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"));
        Assert.False(session.TryGetProperty("graph", out _));
        var rows = session
            .GetProperty("signalLayout")
            .GetProperty("rows");

        Assert.Equal(SignalRowIds.Imu, rows[0].GetProperty("rowId").GetString());
        Assert.False(rows[0].GetProperty("isExpanded").GetBoolean());
        Assert.Equal(SignalRowIds.Velocity, rows[0].GetProperty("children")[0].GetProperty("rowId").GetString());
        Assert.Equal(SignalRowIds.Travel, rows[1].GetProperty("rowId").GetString());
        Assert.False(rows[1].GetProperty("children")[0].GetProperty("isExpanded").GetBoolean());
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsLayoutAndSignalPaneRatios()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var signalLayout = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(
                SignalRowIds.Travel,
                heightRatio: 0.25),
            new SignalLayoutRowPreferences(
                SignalRowIds.Imu,
                heightRatio: 0.75),
        ]);
        var layout = new SessionLayoutPreferences(
            desktopShellRows: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.SignalsMediaArea, 0.6),
                new SessionPaneSizePreference(SessionLayoutPaneIds.AnalysisSidebarArea, 0.4),
            ]),
            desktopSignalsMediaColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Signals, 0.7),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.3, IsCollapsed: true),
            ]),
            desktopAnalysisSidebarColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Analysis, 0.65),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.35),
            ]),
            desktopMediaRows: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.45),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.55),
            ]));

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalLayout = signalLayout,
            Layout = layout,
        });

        var reloaded = new AppPreferences(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        Assert.Equal(signalLayout, stored.SignalLayout);
        Assert.Equal(layout, stored.Layout);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var session = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"));

        Assert.False(session.TryGetProperty("graph", out _));
        Assert.Equal(0.25, session.GetProperty("signalLayout").GetProperty("rows")[0].GetProperty("heightRatio").GetDouble());
        var layoutJson = session.GetProperty("layout");
        Assert.False(layoutJson.TryGetProperty("desktopGraphMediaColumns", out _));
        Assert.Equal(
            SessionLayoutPaneIds.Signals,
            layoutJson.GetProperty("desktopSignalsMediaColumns").GetProperty("panes")[0].GetProperty("paneId").GetString());
        Assert.Equal(
            0.3,
            layoutJson.GetProperty("desktopSignalsMediaColumns").GetProperty("panes")[1].GetProperty("ratio").GetDouble());
        Assert.True(
            layoutJson.GetProperty("desktopSignalsMediaColumns").GetProperty("panes")[1].GetProperty("isCollapsed").GetBoolean());
        Assert.False(
            layoutJson.GetProperty("desktopSignalsMediaColumns").GetProperty("panes")[0].GetProperty("isCollapsed").GetBoolean());
        Assert.Equal(
            SessionLayoutPaneIds.ExtensionMedia,
            layoutJson.GetProperty("desktopMediaRows").GetProperty("panes")[1].GetProperty("paneId").GetString());
    }

    [Fact]
    public async Task SessionPreferences_LoadsLegacyLayoutPaneEntriesWithoutCollapsedStateAsExpanded()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        await File.WriteAllTextAsync(
            preferencesPath,
            $$"""
            {
              "version": 1,
              "session": {
                "sessions": {
                  "{{sessionId:D}}": {
                    "layout": {
                      "desktopGraphMediaColumns": {
                        "panes": [
                          {
                            "paneId": "{{SessionLayoutPaneIds.LegacyGraph}}",
                            "ratio": 0.7
                          },
                          {
                            "paneId": "{{SessionLayoutPaneIds.Media}}",
                            "ratio": 0.3
                          }
                        ]
                      }
                    }
                  }
                }
              }
            }
            """);
        var preferences = new AppPreferences(preferencesPath);

        var stored = await preferences.Session.GetRecordedAsync(sessionId);

        Assert.NotNull(stored.Layout.DesktopSignalsMediaColumns);
        Assert.All(stored.Layout.DesktopSignalsMediaColumns!.Panes, pane => Assert.False(pane.IsCollapsed));
    }

    [Fact]
    public async Task SessionPreferences_LoadsLegacyDocumentPreferenceKeys()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        await File.WriteAllTextAsync(
            preferencesPath,
            $$"""
            {
              "version": 1,
              "session": {
                "sessions": {
                  "{{sessionId:D}}": {
                    "plots": {
                      "travel": false,
                      "travelSmoothing": "Strong"
                    },
                    "statistics": {
                      "travelHistogramMode": "DynamicSag",
                      "sessionAnalysisTargetProfile": "Enduro"
                    },
                    "graph": {
                      "rows": [
                        {
                          "rowId": "{{SignalRowIds.Travel}}",
                          "isExpanded": false,
                          "heightRatio": 0.4,
                          "children": [
                            {
                              "rowId": "{{SignalRowIds.Velocity}}"
                            }
                          ]
                        }
                      ]
                    },
                    "layout": {
                      "desktopGraphMediaColumns": {
                        "panes": [
                          {
                            "paneId": "{{SessionLayoutPaneIds.LegacyGraph}}",
                            "ratio": 0.7
                          },
                          {
                            "paneId": "{{SessionLayoutPaneIds.Media}}",
                            "ratio": 0.3
                          }
                        ]
                      }
                    }
                  }
                }
              }
            }
            """);
        var preferences = new AppPreferences(preferencesPath);

        var stored = await preferences.Session.GetRecordedAsync(sessionId);

        Assert.False(stored.SignalDisplay.Travel);
        Assert.Equal(PlotSmoothingLevel.Strong, stored.SignalDisplay.TravelSmoothing);
        Assert.Equal(TravelDistributionMode.DynamicSag, stored.Analysis.TravelDistributionMode);
        Assert.Equal(SessionInsightsTargetProfile.Enduro, stored.Analysis.SessionInsightsTargetProfile);
        Assert.Equal(SignalRowIds.Travel, stored.SignalLayout.Rows[0].RowId);
        Assert.False(stored.SignalLayout.Rows[0].IsExpanded);
        Assert.Equal(0.4, stored.SignalLayout.Rows[0].HeightRatio);
        Assert.Equal(SignalRowIds.Velocity, stored.SignalLayout.Rows[0].Children[0].RowId);
        Assert.NotNull(stored.Layout.DesktopSignalsMediaColumns);
        Assert.Equal(SessionLayoutPaneIds.Signals, stored.Layout.DesktopSignalsMediaColumns!.Panes[0].PaneId);
    }

    [Fact]
    public async Task SessionPreferences_DocumentNewKeysWinOverLegacyKeys()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        await File.WriteAllTextAsync(
            preferencesPath,
            $$"""
            {
              "version": 2,
              "session": {
                "sessions": {
                  "{{sessionId:D}}": {
                    "plots": {
                      "travel": true
                    },
                    "signalDisplay": {
                      "travel": false
                    },
                    "statistics": {
                      "travelHistogramMode": "DynamicSag",
                      "sessionAnalysisTargetProfile": "Enduro"
                    },
                    "analysis": {
                      "travelDistributionMode": "ActiveSuspension",
                      "sessionInsightsTargetProfile": "DH"
                    },
                    "graph": {
                      "rows": [
                        {
                          "rowId": "{{SignalRowIds.Imu}}"
                        }
                      ]
                    },
                    "signalLayout": {
                      "rows": [
                        {
                          "rowId": "{{SignalRowIds.Speed}}"
                        }
                      ]
                    },
                    "layout": {
                      "desktopGraphMediaColumns": {
                        "panes": [
                          {
                            "paneId": "{{SessionLayoutPaneIds.LegacyGraph}}",
                            "ratio": 0.2
                          },
                          {
                            "paneId": "{{SessionLayoutPaneIds.Media}}",
                            "ratio": 0.8
                          }
                        ]
                      },
                      "desktopSignalsMediaColumns": {
                        "panes": [
                          {
                            "paneId": "{{SessionLayoutPaneIds.Signals}}",
                            "ratio": 0.75
                          },
                          {
                            "paneId": "{{SessionLayoutPaneIds.Media}}",
                            "ratio": 0.25
                          }
                        ]
                      }
                    }
                  }
                }
              }
            }
            """);
        var preferences = new AppPreferences(preferencesPath);

        var stored = await preferences.Session.GetRecordedAsync(sessionId);

        Assert.False(stored.SignalDisplay.Travel);
        Assert.Equal(TravelDistributionMode.ActiveSuspension, stored.Analysis.TravelDistributionMode);
        Assert.Equal(SessionInsightsTargetProfile.DH, stored.Analysis.SessionInsightsTargetProfile);
        Assert.Equal(SignalRowIds.Speed, stored.SignalLayout.Rows[0].RowId);
        Assert.NotNull(stored.Layout.DesktopSignalsMediaColumns);
        Assert.Equal(0.75, stored.Layout.DesktopSignalsMediaColumns!.Panes[0].Ratio);
    }

    [Fact]
    public async Task SessionPreferences_DocumentWritesNewOnlyKeysByDefault()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Travel = false },
            Analysis = current.Analysis with
            {
                TravelDistributionMode = TravelDistributionMode.DynamicSag,
                SessionInsightsTargetProfile = SessionInsightsTargetProfile.Enduro,
            },
            SignalLayout = new SignalLayoutPreferences(
            [
                new SignalLayoutRowPreferences(SignalRowIds.Imu, isExpanded: false),
            ]),
            Layout = new SessionLayoutPreferences(
                desktopSignalsMediaColumns: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Signals, 0.7),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.3),
                ])),
        });

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        Assert.Equal(AppPreferenceSerialization.CurrentVersion, json.RootElement.GetProperty("version").GetInt32());
        var session = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"));

        Assert.False(session.TryGetProperty("plots", out _));
        Assert.False(session.TryGetProperty("statistics", out _));
        Assert.False(session.TryGetProperty("graph", out _));
        Assert.True(session.TryGetProperty("signalDisplay", out _));
        Assert.True(session.TryGetProperty("analysis", out var analysis));
        Assert.True(session.TryGetProperty("signalLayout", out _));
        Assert.False(analysis.TryGetProperty("travelHistogramMode", out _));
        Assert.False(analysis.TryGetProperty("sessionAnalysisTargetProfile", out _));
        Assert.Equal("DynamicSag", analysis.GetProperty("travelDistributionMode").GetString());
        Assert.Equal("Enduro", analysis.GetProperty("sessionInsightsTargetProfile").GetString());

        var layout = session.GetProperty("layout");
        Assert.False(layout.TryGetProperty("desktopGraphMediaColumns", out _));
        var panes = layout.GetProperty("desktopSignalsMediaColumns").GetProperty("panes");
        Assert.Equal(SessionLayoutPaneIds.Signals, panes[0].GetProperty("paneId").GetString());
    }

    [Fact]
    public void SessionPreferences_SyncModelDeserializesLegacyKeys()
    {
        const string json = """
            {
              "plots": {
                "travel": false,
                "travel_smoothing": "strong"
              },
              "statistics": {
                "travel_histogram_mode": "dynamic_sag",
                "session_analysis_target_profile": "enduro"
              },
              "graph": {
                "rows": [
                  {
                    "row_id": "travel",
                    "is_expanded": false
                  }
                ]
              },
              "layout": {
                "desktop_graph_media_columns": {
                  "panes": [
                    {
                      "pane_id": "graph",
                      "ratio": 0.7
                    },
                    {
                      "pane_id": "media",
                      "ratio": 0.3
                    }
                  ]
                }
              }
            }
            """;

        var preferences = JsonSerializer.Deserialize<SessionPreferences>(json, AppJson.Options);

        Assert.NotNull(preferences);
        Assert.False(preferences!.SignalDisplay.Travel);
        Assert.Equal(PlotSmoothingLevel.Strong, preferences.SignalDisplay.TravelSmoothing);
        Assert.Equal(TravelDistributionMode.DynamicSag, preferences.Analysis.TravelDistributionMode);
        Assert.Equal(SessionInsightsTargetProfile.Enduro, preferences.Analysis.SessionInsightsTargetProfile);
        Assert.Equal(SignalRowIds.Travel, preferences.SignalLayout.Rows[0].RowId);
        Assert.False(preferences.SignalLayout.Rows[0].IsExpanded);
        Assert.NotNull(preferences.Layout.DesktopSignalsMediaColumns);
        Assert.Equal(SessionLayoutPaneIds.Signals, preferences.Layout.DesktopSignalsMediaColumns!.Panes[0].PaneId);
    }

    [Fact]
    public void SessionPreferences_SyncModelWritesNewOnlyKeysByDefault()
    {
        var preferences = new SessionPreferences(
            signalDisplay: new SignalDisplayPreferences(Travel: false),
            analysis: new AnalysisPreferences(
                TravelDistributionMode.DynamicSag,
                SessionInsightsTargetProfile: SessionInsightsTargetProfile.Enduro),
            signalLayout: new SignalLayoutPreferences(
            [
                new SignalLayoutRowPreferences(SignalRowIds.Imu),
            ]),
            layout: new SessionLayoutPreferences(
                desktopSignalsMediaColumns: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Signals, 0.7),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.3),
                ])));

        using var json = JsonDocument.Parse(AppJson.Serialize(preferences));

        Assert.False(json.RootElement.TryGetProperty("plots", out _));
        Assert.False(json.RootElement.TryGetProperty("statistics", out _));
        Assert.False(json.RootElement.TryGetProperty("graph", out _));
        Assert.True(json.RootElement.TryGetProperty("signal_display", out _));
        Assert.True(json.RootElement.TryGetProperty("analysis", out var analysis));
        Assert.True(json.RootElement.TryGetProperty("signal_layout", out _));
        Assert.False(analysis.TryGetProperty("travel_histogram_mode", out _));
        Assert.False(analysis.TryGetProperty("session_analysis_target_profile", out _));

        var layout = json.RootElement.GetProperty("layout");
        Assert.False(layout.TryGetProperty("desktop_graph_media_columns", out _));
        var panes = layout.GetProperty("desktop_signals_media_columns").GetProperty("panes");
        Assert.Equal(SessionLayoutPaneIds.Signals, panes[0].GetProperty("pane_id").GetString());
    }

    [Fact]
    public void SignalRowIds_KeepPersistedStringValues()
    {
        Assert.Equal("travel", SignalRowIds.Travel);
        Assert.Equal("velocity", SignalRowIds.Velocity);
        Assert.Equal("imu", SignalRowIds.Imu);
        Assert.Equal("pitch_roll", SignalRowIds.PitchRoll);
        Assert.Equal("speed", SignalRowIds.Speed);
        Assert.Equal("elevation", SignalRowIds.Elevation);
    }

    [Fact]
    public void SessionPaneGroupPreferences_NormalizesLegacyPaneIdsAndPrefersNewDuplicates()
    {
        var preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference(SessionLayoutPaneIds.LegacyGraph, 0.2),
            new SessionPaneSizePreference(SessionLayoutPaneIds.Signals, 0.8, IsCollapsed: true),
            new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.2),
        ]);

        Assert.Equal(2, preferences.Panes.Count);
        Assert.Equal(SessionLayoutPaneIds.Signals, preferences.Panes[0].PaneId);
        Assert.Equal(0.8, preferences.Panes[0].Ratio);
        Assert.True(preferences.Panes[0].IsCollapsed);
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_SerializesConcurrentUpdates()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        var preferences = new AppPreferences(preferencesPath);

        await Task.WhenAll(sessionIds.Select((sessionId, index) =>
            preferences.Session.UpdateRecordedAsync(sessionId, current => current with
            {
                SignalDisplay = current.SignalDisplay with { Travel = index % 2 == 0 },
                Analysis = current.Analysis with { VelocityAverageMode = VelocityAverageMode.StrokePeakAveraged },
            })));

        foreach (var sessionId in sessionIds)
        {
            var stored = await preferences.Session.GetRecordedAsync(sessionId);
            Assert.Equal(VelocityAverageMode.StrokePeakAveraged, stored.Analysis.VelocityAverageMode);
        }

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var sessions = json.RootElement.GetProperty("session").GetProperty("sessions");
        Assert.Equal(sessionIds.Length, sessions.EnumerateObject().Count());
    }

    [Fact]
    public async Task SessionPreferences_ReturnDefaults_ForMalformedJson_AndOverwriteOnNextUpdate()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        await File.WriteAllTextAsync(preferencesPath, "{ not valid json");
        var preferences = new AppPreferences(preferencesPath);

        var stored = await preferences.Session.GetRecordedAsync(sessionId);
        AssertDefaultSessionPreferences(stored);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Travel = false },
        });

        var updated = await preferences.Session.GetRecordedAsync(sessionId);
        Assert.False(updated.SignalDisplay.Travel);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        Assert.True(json.RootElement.TryGetProperty("session", out _));
    }

    [Fact]
    public async Task SessionPreferences_ReturnDefaults_ForUnknownEnumValues()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        await File.WriteAllTextAsync(
            preferencesPath,
            $$"""
            {
              "version": 1,
              "session": {
                "sessions": {
                  "{{sessionId:D}}": {
                    "plots": {
                      "travel": false,
                      "velocity": true,
                                                "imu": false,
                                                "speed": false,
                                                "elevation": true,
                                                "travelSmoothing": "MissingMode",
                                                "velocitySmoothing": "MissingMode",
                                                "imuSmoothing": "MissingMode",
                                                "speedSmoothing": "MissingMode",
                                                "elevationSmoothing": "MissingMode"
                    },
                    "statistics": {
                      "travelHistogramMode": "MissingMode",
                      "velocityAverageMode": "MissingMode",
                      "balanceDisplacementMode": "MissingMode",
                      "sessionAnalysisTargetProfile": "MissingMode"
                    }
                  }
                }
              }
            }
            """);
        var preferences = new AppPreferences(preferencesPath);

        var stored = await preferences.Session.GetRecordedAsync(sessionId);

        Assert.False(stored.SignalDisplay.Travel);
        Assert.True(stored.SignalDisplay.Velocity);
        Assert.False(stored.SignalDisplay.Imu);
        Assert.False(stored.SignalDisplay.Speed);
        Assert.True(stored.SignalDisplay.Elevation);
        Assert.Equal(PlotSmoothingLevel.Off, stored.SignalDisplay.TravelSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.SignalDisplay.VelocitySmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.SignalDisplay.ImuSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.SignalDisplay.SpeedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.SignalDisplay.ElevationSmoothing);
        Assert.Equal(TravelDistributionMode.ActiveSuspension, stored.Analysis.TravelDistributionMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, stored.Analysis.VelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Zenith, stored.Analysis.BalanceDisplacementMode);
        Assert.Equal(SessionInsightsTargetProfile.Trail, stored.Analysis.SessionInsightsTargetProfile);
    }

    [Fact]
    public async Task GetSyncDataAsync_ReturnsChangedPreferencesSnapshot()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var layer = new TileLayerConfig
        {
            Id = Guid.NewGuid(),
            Name = "Trail maps",
            UrlTemplate = "https://tiles.example/{z}/{x}/{y}.png",
            AttributionText = "Example tiles",
            AttributionUrl = "https://tiles.example",
            MaxZoom = 18,
            IsCustom = true,
        };

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Map.SetSelectedLayerIdAsync(layer.Id);
        await preferences.Map.SetCustomLayersAsync([layer]);
        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Velocity = false },
        });

        var snapshot = await preferences.GetSyncDataAsync(0);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Updated > 0);
        Assert.Equal(layer.Id, snapshot.Maps.SelectedLayerId);
        Assert.Single(snapshot.Maps.CustomLayers);
        Assert.False(snapshot.Session.Sessions[sessionId].SignalDisplay.Velocity);
    }

    [Fact]
    public async Task GetSyncDataAsync_MigratesLegacyDocumentWithoutUpdatedTimestamp()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var selectedLayerId = Guid.NewGuid();

        await File.WriteAllTextAsync(
            preferencesPath,
            $$"""
            {
              "version": 1,
              "maps": {
                "selectedLayerId": "{{selectedLayerId:D}}",
                "customLayers": []
              }
            }
            """);
        var preferences = new AppPreferences(preferencesPath);

        var snapshot = await preferences.GetSyncDataAsync(1);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Updated > 1);
        Assert.Equal(selectedLayerId, snapshot.Maps.SelectedLayerId);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        Assert.True(json.RootElement.GetProperty("updated").GetInt64() > 1);
    }

    [Fact]
    public async Task ApplySyncDataAsync_AppliesNewerSnapshotAndIgnoresOlderSnapshot()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var selectedLayerId = Guid.NewGuid();
        var olderLayerId = Guid.NewGuid();

        var preferences = new AppPreferences(preferencesPath);

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Maps = new MapPreferencesSyncData
            {
                SelectedLayerId = selectedLayerId,
            },
            Session = new SessionPreferencesSyncData
            {
                Sessions =
                {
                    [sessionId] = SessionPreferences.Default with
                    {
                        Analysis = SessionPreferences.Default.Analysis with
                        {
                            TravelDistributionMode = TravelDistributionMode.DynamicSag,
                        },
                    },
                },
            },
        });

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 99,
            Maps = new MapPreferencesSyncData
            {
                SelectedLayerId = olderLayerId,
            },
        });

        Assert.Equal(selectedLayerId, await preferences.Map.GetSelectedLayerIdAsync());
        var stored = await preferences.Session.GetRecordedAsync(sessionId);
        Assert.Equal(TravelDistributionMode.DynamicSag, stored.Analysis.TravelDistributionMode);
    }

    [Fact]
    public async Task SyncDataApplied_EmitsOncePerSuccessfulApply()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);
        var emissions = 0;
        using var subscription = preferences.SyncDataApplied.Subscribe(_ => Interlocked.Increment(ref emissions));

        // null payload: short-circuits before any I/O — must not emit.
        await preferences.ApplySyncDataAsync(null);
        Assert.Equal(0, emissions);

        // Newer payload applies → one emission.
        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Maps = new MapPreferencesSyncData { SelectedLayerId = Guid.NewGuid() },
        });
        Assert.Equal(1, emissions);

        // Older payload skips (Updated < document.Updated) → no emission.
        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 50,
            Maps = new MapPreferencesSyncData { SelectedLayerId = Guid.NewGuid() },
        });
        Assert.Equal(1, emissions);
    }

    [Fact]
    public async Task SyncDataApplied_DoesNotReplayHistory_OnLateSubscribe()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);

        // Apply before subscribing — late subscribers should not see this.
        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Maps = new MapPreferencesSyncData { SelectedLayerId = Guid.NewGuid() },
        });

        var emissions = 0;
        using var subscription = preferences.SyncDataApplied.Subscribe(_ => Interlocked.Increment(ref emissions));
        Assert.Equal(0, emissions);
    }

    [Fact]
    public async Task ObserveRecorded_EmitsOnSyncApply()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<SessionPreferences>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncEmission = new TaskCompletionSource<SessionPreferences>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Session.ObserveRecorded(sessionId)
            .Subscribe(value =>
            {
                if (Interlocked.Increment(ref emissions) == 1)
                {
                    initialEmission.TrySetResult(value);
                }
                else
                {
                    syncEmission.TrySetResult(value);
                }
            });

        AssertDefaultSessionPreferences(await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Session = new SessionPreferencesSyncData
            {
                Sessions =
                {
                    [sessionId] = SessionPreferences.Default with
                    {
                        Analysis = SessionPreferences.Default.Analysis with
                        {
                            TravelDistributionMode = TravelDistributionMode.DynamicSag,
                        },
                    },
                },
            },
        });

        var observed = await syncEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TravelDistributionMode.DynamicSag, observed.Analysis.TravelDistributionMode);
    }

    [Fact]
    public async Task ObserveRecorded_EmitsOnLocalUpdate()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<SessionPreferences>(TaskCreationOptions.RunContinuationsAsynchronously);
        var localEmission = new TaskCompletionSource<SessionPreferences>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Session.ObserveRecorded(sessionId)
            .Subscribe(value =>
            {
                if (Interlocked.Increment(ref emissions) == 1)
                {
                    initialEmission.TrySetResult(value);
                }
                else
                {
                    localEmission.TrySetResult(value);
                }
            });

        AssertDefaultSessionPreferences(await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await preferences.Session.UpdateRecordedAsync(sessionId, current =>
            current with
            {
                Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag },
            });

        var observed = await localEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TravelDistributionMode.DynamicSag, observed.Analysis.TravelDistributionMode);
    }

    [Fact]
    public async Task ObserveRecorded_ReplaysCurrentValue_OnSubscribe()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current =>
            current with
            {
                Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag },
            });

        var observed = await preferences.Session.ObserveRecorded(sessionId)
            .FirstAsync()
            .ToTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TravelDistributionMode.DynamicSag, observed.Analysis.TravelDistributionMode);
    }

    [Fact]
    public async Task ObserveRecordedChanges_ReportsWriteOriginAndSyncClock()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<PreferenceValueChange<SessionPreferences>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var localEmission = new TaskCompletionSource<PreferenceValueChange<SessionPreferences>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetEmission = new TaskCompletionSource<PreferenceValueChange<SessionPreferences>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Session.ObserveRecordedChanges(sessionId)
            .Subscribe(change =>
            {
                var emission = Interlocked.Increment(ref emissions);
                if (emission == 1)
                {
                    initialEmission.TrySetResult(change);
                }
                else if (emission == 2)
                {
                    localEmission.TrySetResult(change);
                }
                else
                {
                    resetEmission.TrySetResult(change);
                }
            });

        var initial = await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, initial.Origin);
        Assert.False(initial.AdvancesSyncClock);
        AssertDefaultSessionPreferences(initial.Value);

        await preferences.Session.UpdateRecordedAsync(sessionId, current =>
            current with
            {
                Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag },
                Processing = new SessionProcessingPreferences(VelocityFilterWindowMilliseconds: 250),
            });

        var local = await localEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.LocalWrite, local.Origin);
        Assert.True(local.AdvancesSyncClock);
        Assert.Equal(TravelDistributionMode.DynamicSag, local.Value.Analysis.TravelDistributionMode);
        Assert.Equal(250, local.Value.Processing.VelocityFilterWindowMilliseconds);

        await preferences.Session.ResetRecordedProcessingToDefaultLocallyAsync(sessionId);

        var reset = await resetEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.LocalNoSyncClockWrite, reset.Origin);
        Assert.False(reset.AdvancesSyncClock);
        Assert.Equal(TravelDistributionMode.DynamicSag, reset.Value.Analysis.TravelDistributionMode);
        Assert.Equal(
            TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds,
            reset.Value.Processing.VelocityFilterWindowMilliseconds);
    }

    [Fact]
    public async Task ObserveRecordedChanges_EmitsNoClockReset_WhenValueMatchesReplay()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<PreferenceValueChange<SessionPreferences>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetEmission = new TaskCompletionSource<PreferenceValueChange<SessionPreferences>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Session.ObserveRecordedChanges(sessionId)
            .Subscribe(change =>
            {
                if (Interlocked.Increment(ref emissions) == 1)
                {
                    initialEmission.TrySetResult(change);
                }
                else
                {
                    resetEmission.TrySetResult(change);
                }
            });

        var initial = await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, initial.Origin);
        AssertDefaultSessionPreferences(initial.Value);

        await preferences.Session.ResetRecordedProcessingToDefaultLocallyAsync(sessionId);

        var reset = await resetEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.LocalNoSyncClockWrite, reset.Origin);
        Assert.False(reset.AdvancesSyncClock);
        AssertDefaultSessionPreferences(reset.Value);
    }

    [Fact]
    public async Task ObserveAllRecordedChanges_ReplaysAndEmitsLocalWrites()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var localEmission = new TaskCompletionSource<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Session.ObserveAllRecordedChanges()
            .Subscribe(change =>
            {
                if (Interlocked.Increment(ref emissions) == 1)
                {
                    initialEmission.TrySetResult(change);
                }
                else
                {
                    localEmission.TrySetResult(change);
                }
            });

        var initial = await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.SyncApply, initial.Origin);
        Assert.False(initial.AdvancesSyncClock);
        Assert.Empty(initial.Value);

        await preferences.Session.UpdateRecordedAsync(sessionId, current =>
            current with
            {
                Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag },
            });

        var local = await localEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PreferenceChangeOrigin.LocalWrite, local.Origin);
        Assert.True(local.AdvancesSyncClock);
        Assert.True(local.Value.TryGetValue(sessionId, out var stored));
        Assert.Equal(TravelDistributionMode.DynamicSag, stored.Analysis.TravelDistributionMode);
    }

    [Fact]
    public async Task ObserveAllRecordedChanges_DistinctUntilChanged_SquashesIdenticalRefresh()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var initialEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStoredEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emissions = 0;
        using var subscription = preferences.Session.ObserveAllRecordedChanges()
            .Subscribe(_ =>
            {
                var emission = Interlocked.Increment(ref emissions);
                if (emission == 1)
                {
                    initialEmission.TrySetResult();
                }
                else if (emission == 2)
                {
                    firstStoredEmission.TrySetResult();
                }
                else
                {
                    duplicateEmission.TrySetResult();
                }
            });
        await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stored = SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with
            {
                TravelDistributionMode = TravelDistributionMode.DynamicSag,
            },
        };
        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Session = new SessionPreferencesSyncData
            {
                Sessions = { [sessionId] = stored },
            },
        });
        await firstStoredEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 101,
            Session = new SessionPreferencesSyncData
            {
                Sessions = { [sessionId] = stored },
            },
        });

        await AssertDoesNotCompleteAsync(duplicateEmission.Task, TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, emissions);
    }

    [Fact]
    public async Task ApplySyncData_OverridesThemeMode_WhenIncomingUpdatedIsNewer()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");

        var preferences = new AppPreferences(preferencesPath);
        Assert.Equal(SufniThemeMode.Dark, await preferences.Theme.GetModeAsync());

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 200,
            Theme = new ThemePreferencesSyncData { Mode = "Light" },
        });

        Assert.Equal(SufniThemeMode.Light, await preferences.Theme.GetModeAsync());
    }

    [Fact]
    public async Task ObserveRecorded_DistinctUntilChanged_SquashesIdenticalRefresh()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var emissions = 0;
        var initialEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStoredEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = preferences.Session.ObserveRecorded(sessionId)
            .Subscribe(_ =>
            {
                var emission = Interlocked.Increment(ref emissions);
                if (emission == 1)
                {
                    initialEmission.TrySetResult();
                }
                else if (emission == 2)
                {
                    firstStoredEmission.TrySetResult();
                }
                else
                {
                    duplicateEmission.TrySetResult();
                }
            });
        await initialEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stored = SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with
            {
                TravelDistributionMode = TravelDistributionMode.DynamicSag,
            },
        };
        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Session = new SessionPreferencesSyncData
            {
                Sessions = { [sessionId] = stored },
            },
        });
        await firstStoredEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 101,
            Session = new SessionPreferencesSyncData
            {
                Sessions = { [sessionId] = stored },
            },
        });

        await AssertDoesNotCompleteAsync(duplicateEmission.Task, TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, emissions);
    }

    private static async Task AssertDoesNotCompleteAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        Assert.NotSame(task, completed);
    }

    [Fact]
    public void SynchronizationData_SerializesAppPreferencesSnapshot()
    {
        var selectedLayerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var data = new SynchronizationData
        {
            AppPreferences = new AppPreferencesSyncData
            {
                Updated = 42,
                Maps = new MapPreferencesSyncData
                {
                    SelectedLayerId = selectedLayerId,
                },
                Session = new SessionPreferencesSyncData
                {
                    Sessions =
                    {
                        [sessionId] = SessionPreferences.Default with
                        {
                            SignalDisplay = SessionPreferences.Default.SignalDisplay with
                            {
                                TravelSmoothing = PlotSmoothingLevel.Strong,
                            },
                            SignalLayout = new SignalLayoutPreferences(
                            [
                                new SignalLayoutRowPreferences(SignalRowIds.Imu, isExpanded: false),
                            ]),
                        },
                    },
                },
            },
        };

        var roundTripped = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(data));

        Assert.NotNull(roundTripped?.AppPreferences);
        Assert.Equal(42, roundTripped!.AppPreferences!.Updated);
        Assert.Equal(selectedLayerId, roundTripped.AppPreferences.Maps.SelectedLayerId);
        Assert.Equal(PlotSmoothingLevel.Strong, roundTripped.AppPreferences.Session.Sessions[sessionId].SignalDisplay.TravelSmoothing);
        Assert.False(roundTripped.AppPreferences.Session.Sessions[sessionId].SignalLayout.Rows[0].IsExpanded);
    }

    private static void AssertDefaultSessionPreferences(SessionPreferences preferences)
    {
        Assert.True(preferences.SignalDisplay.Travel);
        Assert.True(preferences.SignalDisplay.Velocity);
        Assert.True(preferences.SignalDisplay.Imu);
        Assert.True(preferences.SignalDisplay.PitchRoll);
        Assert.True(preferences.SignalDisplay.Speed);
        Assert.True(preferences.SignalDisplay.Elevation);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.SignalDisplay.TravelSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.SignalDisplay.VelocitySmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.SignalDisplay.ImuSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.SignalDisplay.PitchRollSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.SignalDisplay.SpeedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.SignalDisplay.ElevationSmoothing);
        Assert.Equal(TravelDistributionMode.ActiveSuspension, preferences.Analysis.TravelDistributionMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, preferences.Analysis.VelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Zenith, preferences.Analysis.BalanceDisplacementMode);
        Assert.Equal(SessionInsightsTargetProfile.Trail, preferences.Analysis.SessionInsightsTargetProfile);
        Assert.Equal(
            TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds,
            preferences.Processing.VelocityFilterWindowMilliseconds);
        Assert.Equal(
            [SignalRowIds.Travel, SignalRowIds.Imu, SignalRowIds.Speed],
            preferences.SignalLayout.Rows.Select(row => row.RowId).ToArray());
        Assert.Equal([SignalRowIds.Velocity], preferences.SignalLayout.Rows[0].Children.Select(row => row.RowId).ToArray());
        Assert.Equal([SignalRowIds.PitchRoll], preferences.SignalLayout.Rows[1].Children.Select(row => row.RowId).ToArray());
        Assert.Equal([SignalRowIds.Elevation], preferences.SignalLayout.Rows[2].Children.Select(row => row.RowId).ToArray());
        Assert.All(preferences.SignalLayout.Rows, row => Assert.True(row.IsExpanded));
        Assert.Null(preferences.Layout.DesktopShellRows);
        Assert.Null(preferences.Layout.DesktopSignalsMediaColumns);
        Assert.Null(preferences.Layout.DesktopAnalysisSidebarColumns);
        Assert.Null(preferences.Layout.DesktopMediaRows);
    }

}
