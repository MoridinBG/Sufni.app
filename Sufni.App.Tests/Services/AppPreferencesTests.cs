using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Sufni.App.Tests.TestSupport;
using Sufni.App.Theming;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Tests.Services;

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

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var maps = json.RootElement.GetProperty("maps");
        Assert.Equal(layer.Id.ToString("D"), maps.GetProperty("selectedLayerId").GetString());
        Assert.Single(maps.GetProperty("customLayers").EnumerateArray());
        Assert.Empty(Directory.EnumerateFiles(tempDirectory.Path, "*.tmp"));
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
    public async Task SessionPreferences_UpdateRecorded_PersistsAllPlotVisibilityValues()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            Plots = current.Plots with
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

        Assert.False(stored.Plots.Travel);
        Assert.True(stored.Plots.Velocity);
        Assert.False(stored.Plots.Imu);
        Assert.True(stored.Plots.Speed);
        Assert.False(stored.Plots.Elevation);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var plots = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"))
            .GetProperty("plots");
        Assert.False(plots.GetProperty("travel").GetBoolean());
        Assert.True(plots.GetProperty("velocity").GetBoolean());
        Assert.False(plots.GetProperty("imu").GetBoolean());
        Assert.True(plots.GetProperty("speed").GetBoolean());
        Assert.False(plots.GetProperty("elevation").GetBoolean());
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
            Plots = current.Plots with { Imu = false },
        });
        await preferences.Session.UpdateRecordedAsync(secondSessionId, current => current with
        {
            Statistics = current.Statistics with
            {
                TravelHistogramMode = TravelHistogramMode.DynamicSag,
                SessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Enduro,
            },
        });

        var first = await preferences.Session.GetRecordedAsync(firstSessionId);
        var second = await preferences.Session.GetRecordedAsync(secondSessionId);

        Assert.False(first.Plots.Imu);
        Assert.Equal(TravelHistogramMode.ActiveSuspension, first.Statistics.TravelHistogramMode);
        Assert.True(second.Plots.Imu);
        Assert.Equal(TravelHistogramMode.DynamicSag, second.Statistics.TravelHistogramMode);
        Assert.Equal(SessionAnalysisTargetProfile.Enduro, second.Statistics.SessionAnalysisTargetProfile);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var sessions = json.RootElement.GetProperty("session").GetProperty("sessions");
        Assert.Equal(2, sessions.EnumerateObject().Count());
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsPlotSmoothingLevels()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            Plots = current.Plots with
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

        Assert.Equal(PlotSmoothingLevel.Light, stored.Plots.TravelSmoothing);
        Assert.Equal(PlotSmoothingLevel.Strong, stored.Plots.VelocitySmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.Plots.ImuSmoothing);
        Assert.Equal(PlotSmoothingLevel.Light, stored.Plots.SpeedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Strong, stored.Plots.ElevationSmoothing);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var plots = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"))
            .GetProperty("plots");
        Assert.Equal("Light", plots.GetProperty("travelSmoothing").GetString());
        Assert.Equal("Strong", plots.GetProperty("velocitySmoothing").GetString());
        Assert.Equal("Off", plots.GetProperty("imuSmoothing").GetString());
        Assert.Equal("Light", plots.GetProperty("speedSmoothing").GetString());
        Assert.Equal("Strong", plots.GetProperty("elevationSmoothing").GetString());
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
    public async Task SessionPreferences_UpdateRecorded_PersistsGraphHierarchyAndExpansion()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var graph = new SessionGraphPreferences(
        [
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Imu,
                isExpanded: false,
                children:
                [
                    new SessionGraphRowPreferences(TelemetryGraphRowIds.Velocity),
                ]),
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Travel,
                children:
                [
                    new SessionGraphRowPreferences(TelemetryGraphRowIds.Speed, isExpanded: false),
                ]),
        ]);

        var preferences = new AppPreferences(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            Graph = graph,
        });

        var reloaded = new AppPreferences(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        Assert.Equal(graph, stored.Graph);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var rows = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"))
            .GetProperty("graph")
            .GetProperty("rows");

        Assert.Equal(TelemetryGraphRowIds.Imu, rows[0].GetProperty("rowId").GetString());
        Assert.False(rows[0].GetProperty("isExpanded").GetBoolean());
        Assert.Equal(TelemetryGraphRowIds.Velocity, rows[0].GetProperty("children")[0].GetProperty("rowId").GetString());
        Assert.Equal(TelemetryGraphRowIds.Travel, rows[1].GetProperty("rowId").GetString());
        Assert.False(rows[1].GetProperty("children")[0].GetProperty("isExpanded").GetBoolean());
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsLayoutAndGraphPaneRatios()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var graph = new SessionGraphPreferences(
        [
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Travel,
                heightRatio: 0.25),
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Imu,
                heightRatio: 0.75),
        ]);
        var layout = new SessionLayoutPreferences(
            desktopShellRows: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.GraphMediaArea, 0.6),
                new SessionPaneSizePreference(SessionLayoutPaneIds.StatisticsSidebarArea, 0.4),
            ]),
            desktopGraphMediaColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Graph, 0.7),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.3, IsCollapsed: true),
            ]),
            desktopStatisticsSidebarColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Statistics, 0.65),
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
            Graph = graph,
            Layout = layout,
        });

        var reloaded = new AppPreferences(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        Assert.Equal(graph, stored.Graph);
        Assert.Equal(layout, stored.Layout);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath));
        var session = json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"));

        Assert.Equal(0.25, session.GetProperty("graph").GetProperty("rows")[0].GetProperty("heightRatio").GetDouble());
        var layoutJson = session.GetProperty("layout");
        Assert.Equal(
            SessionLayoutPaneIds.Graph,
            layoutJson.GetProperty("desktopGraphMediaColumns").GetProperty("panes")[0].GetProperty("paneId").GetString());
        Assert.Equal(
            0.3,
            layoutJson.GetProperty("desktopGraphMediaColumns").GetProperty("panes")[1].GetProperty("ratio").GetDouble());
        Assert.True(
            layoutJson.GetProperty("desktopGraphMediaColumns").GetProperty("panes")[1].GetProperty("isCollapsed").GetBoolean());
        Assert.False(
            layoutJson.GetProperty("desktopGraphMediaColumns").GetProperty("panes")[0].GetProperty("isCollapsed").GetBoolean());
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
                            "paneId": "{{SessionLayoutPaneIds.Graph}}",
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

        Assert.NotNull(stored.Layout.DesktopGraphMediaColumns);
        Assert.All(stored.Layout.DesktopGraphMediaColumns!.Panes, pane => Assert.False(pane.IsCollapsed));
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
                Plots = current.Plots with { Travel = index % 2 == 0 },
                Statistics = current.Statistics with { VelocityAverageMode = VelocityAverageMode.StrokePeakAveraged },
            })));

        foreach (var sessionId in sessionIds)
        {
            var stored = await preferences.Session.GetRecordedAsync(sessionId);
            Assert.Equal(VelocityAverageMode.StrokePeakAveraged, stored.Statistics.VelocityAverageMode);
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
            Plots = current.Plots with { Travel = false },
        });

        var updated = await preferences.Session.GetRecordedAsync(sessionId);
        Assert.False(updated.Plots.Travel);
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

        Assert.False(stored.Plots.Travel);
        Assert.True(stored.Plots.Velocity);
        Assert.False(stored.Plots.Imu);
        Assert.False(stored.Plots.Speed);
        Assert.True(stored.Plots.Elevation);
        Assert.Equal(PlotSmoothingLevel.Off, stored.Plots.TravelSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.Plots.VelocitySmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.Plots.ImuSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.Plots.SpeedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, stored.Plots.ElevationSmoothing);
        Assert.Equal(TravelHistogramMode.ActiveSuspension, stored.Statistics.TravelHistogramMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, stored.Statistics.VelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Zenith, stored.Statistics.BalanceDisplacementMode);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, stored.Statistics.SessionAnalysisTargetProfile);
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
            Plots = current.Plots with { Velocity = false },
        });

        var snapshot = await preferences.GetSyncDataAsync(0);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Updated > 0);
        Assert.Equal(layer.Id, snapshot.Maps.SelectedLayerId);
        Assert.Single(snapshot.Maps.CustomLayers);
        Assert.False(snapshot.Session.Sessions[sessionId].Plots.Velocity);
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
                        Statistics = SessionPreferences.Default.Statistics with
                        {
                            TravelHistogramMode = TravelHistogramMode.DynamicSag,
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
        Assert.Equal(TravelHistogramMode.DynamicSag, stored.Statistics.TravelHistogramMode);
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

        var next = preferences.Session.ObserveRecorded(sessionId).FirstAsync().ToTask();

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Session = new SessionPreferencesSyncData
            {
                Sessions =
                {
                    [sessionId] = SessionPreferences.Default with
                    {
                        Statistics = SessionPreferences.Default.Statistics with
                        {
                            TravelHistogramMode = TravelHistogramMode.DynamicSag,
                        },
                    },
                },
            },
        });

        var observed = await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TravelHistogramMode.DynamicSag, observed.Statistics.TravelHistogramMode);
    }

    [Fact]
    public async Task ObserveRecorded_DoesNotEmitOnLocalUpdate()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = new AppPreferences(preferencesPath);

        var emissions = 0;
        using var subscription = preferences.Session.ObserveRecorded(sessionId)
            .Subscribe(_ => Interlocked.Increment(ref emissions));

        await preferences.Session.UpdateRecordedAsync(sessionId, current =>
            current with
            {
                Statistics = current.Statistics with { TravelHistogramMode = TravelHistogramMode.DynamicSag },
            });

        Assert.Equal(0, emissions);
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
        var firstEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = preferences.Session.ObserveRecorded(sessionId)
            .Subscribe(_ =>
            {
                if (Interlocked.Increment(ref emissions) == 1)
                {
                    firstEmission.TrySetResult();
                }
                else
                {
                    duplicateEmission.TrySetResult();
                }
            });

        var stored = SessionPreferences.Default with
        {
            Statistics = SessionPreferences.Default.Statistics with
            {
                TravelHistogramMode = TravelHistogramMode.DynamicSag,
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
        await firstEmission.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 101,
            Session = new SessionPreferencesSyncData
            {
                Sessions = { [sessionId] = stored },
            },
        });

        await AssertDoesNotCompleteAsync(duplicateEmission.Task, TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, emissions);
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
                            Plots = SessionPreferences.Default.Plots with
                            {
                                TravelSmoothing = PlotSmoothingLevel.Strong,
                            },
                            Graph = new SessionGraphPreferences(
                            [
                                new SessionGraphRowPreferences(TelemetryGraphRowIds.Imu, isExpanded: false),
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
        Assert.Equal(PlotSmoothingLevel.Strong, roundTripped.AppPreferences.Session.Sessions[sessionId].Plots.TravelSmoothing);
        Assert.False(roundTripped.AppPreferences.Session.Sessions[sessionId].Graph.Rows[0].IsExpanded);
    }

    private static void AssertDefaultSessionPreferences(SessionPreferences preferences)
    {
        Assert.True(preferences.Plots.Travel);
        Assert.True(preferences.Plots.Velocity);
        Assert.True(preferences.Plots.Imu);
        Assert.True(preferences.Plots.PitchRoll);
        Assert.True(preferences.Plots.Speed);
        Assert.True(preferences.Plots.Elevation);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.TravelSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.VelocitySmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.ImuSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.PitchRollSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.SpeedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.ElevationSmoothing);
        Assert.Equal(TravelHistogramMode.ActiveSuspension, preferences.Statistics.TravelHistogramMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, preferences.Statistics.VelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Zenith, preferences.Statistics.BalanceDisplacementMode);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, preferences.Statistics.SessionAnalysisTargetProfile);
        Assert.Equal(
            TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds,
            preferences.Processing.VelocityFilterWindowMilliseconds);
        Assert.Equal(
            [TelemetryGraphRowIds.Travel, TelemetryGraphRowIds.Imu, TelemetryGraphRowIds.Speed],
            preferences.Graph.Rows.Select(row => row.RowId).ToArray());
        Assert.Equal([TelemetryGraphRowIds.Velocity], preferences.Graph.Rows[0].Children.Select(row => row.RowId).ToArray());
        Assert.Equal([TelemetryGraphRowIds.PitchRoll], preferences.Graph.Rows[1].Children.Select(row => row.RowId).ToArray());
        Assert.Equal([TelemetryGraphRowIds.Elevation], preferences.Graph.Rows[2].Children.Select(row => row.RowId).ToArray());
        Assert.All(preferences.Graph.Rows, row => Assert.True(row.IsExpanded));
        Assert.Null(preferences.Layout.DesktopShellRows);
        Assert.Null(preferences.Layout.DesktopGraphMediaColumns);
        Assert.Null(preferences.Layout.DesktopStatisticsSidebarColumns);
        Assert.Null(preferences.Layout.DesktopMediaRows);
    }

}
