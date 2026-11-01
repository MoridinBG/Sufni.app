using Microsoft.Extensions.Time.Testing;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Sufni.App.Infrastructure.Theming;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Tests.Infrastructure;

public class AppPreferencesTests
{
    [Fact]
    public async Task Initialization_RemovesLegacySyncFields_AfterCapturingCustomLayersForMigration()
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(path, """
            {"version":3,"updated":77,"maps":{"selectedLayerId":"LAYER_ID","customLayers":[
            {"id":"LAYER_ID","name":"Legacy","url_template":"https://tiles/{z}/{x}/{y}","attribution_text":"Tiles","attribution_url":"https://tiles","max_zoom":18,"is_custom":true}]},"theme":{"mode":"Light"}}
            """.Replace("LAYER_ID", id.ToString()), TestContext.Current.CancellationToken);
        var launch = AppPreferences.ReadLaunchValues(path, UiLayoutProfile.Workspace);
        Assert.Equal((id, 77L), (Assert.Single(launch.LegacyCustomLayers).Id, launch.LegacyCustomLayers[0].Updated));
        var preferences = new AppPreferences(path, launch, new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher());

        await preferences.InitializeAsync();

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.False(json.RootElement.TryGetProperty("updated", out _));
        Assert.False(json.RootElement.GetProperty("maps").TryGetProperty("customLayers", out _));
        Assert.Equal(id, await preferences.Map.GetSelectedLayerIdAsync());
        Assert.Equal(SufniThemeMode.Light, await preferences.Theme.GetModeAsync());
    }

    [Fact]
    public async Task PreferencePublication_WaitsForDispatcher_AfterAtomicFileCommit()
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var dispatcher = new ManualUiThreadDispatcher();
        var preferences = new AppPreferences(path, AppPreferences.ReadLaunchValues(path, UiLayoutProfile.Workspace), new InlineBackgroundTaskRunner(), dispatcher);
        var observed = new List<Guid?>();
        using var subscription = preferences.Map.ObserveChanges().Subscribe(value =>
        {
            Assert.True(dispatcher.CheckAccess());
            observed.Add(value);
        });
        await dispatcher.DrainAsync();
        var selected = Guid.NewGuid();

        var write = preferences.Map.SetSelectedLayerIdAsync(selected);
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new Guid?[] { null }, observed);
        Assert.False(write.IsCompleted);
        Assert.Equal(selected, await TestAppPreferences.Load(path).Map.GetSelectedLayerIdAsync());
        await dispatcher.DrainAsync();
        await write.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new Guid?[] { null, selected }, observed);
    }

    [Fact]
    public async Task FailedFileCommit_PreservesCurrentPreference()
    {
        using var directory = new TempDirectory();
        var preferences = TestAppPreferences.Load(directory.Path);

        await Assert.ThrowsAnyAsync<IOException>(() => preferences.Theme.SetModeAsync(SufniThemeMode.Light));

        Assert.Equal(SufniThemeMode.Dark, await preferences.Theme.GetModeAsync());
    }

    [Fact]
    public async Task Launch_CapturesLegacyFilterForMigration_BeforeRuntimePreferencesDropIt()
    {
        using var temp = new TempDirectory("legacy-processing-preference");
        var path = Path.Combine(temp.Path, "preferences.json");
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(path, $$"""
            { "session": { "sessions": { "{{id:D}}": {
                "processing": { "velocityFilterWindowMilliseconds": 100 }
            } } } }
            """, TestContext.Current.CancellationToken);
        var launch = AppPreferences.ReadLaunchValues(path, UiLayoutProfile.Workspace);
        var preferences = new AppPreferences(path, launch, new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher());

        await preferences.Session.UpdateRecordedAsync(id, value => value with
        {
            Analysis = value.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag }
        });

        Assert.Equal(100, launch.LegacyProcessingWindows[id]);
        Assert.Equal(TravelDistributionMode.DynamicSag, (await preferences.Session.GetRecordedAsync(id)).Analysis.TravelDistributionMode);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.False(saved.RootElement.GetProperty("session").GetProperty("sessions").GetProperty(id.ToString("D"))
            .TryGetProperty("processing", out _));
    }

    public enum RecordedPreferencePersistenceCase
    {
        SignalDisplayVisibility,
        SignalSmoothing,
        SignalLayoutHierarchy,
        LayoutAndPaneRatios,
    }

    public enum RecordedPreferenceFallbackCase
    {
        MalformedJson,
        UnknownEnumValues,
    }

    public enum ObserveRecordedValueCase
    {
        LocalUpdate,
        ReplayCurrentValue,
    }

    [Theory]
    [InlineData(null, UiLayoutProfile.Compact)]
    [InlineData("{broken", UiLayoutProfile.Workspace)]
    [InlineData("{}", UiLayoutProfile.Compact)]
    public async Task LaunchPreferences_UsesPlatformDefaults_WhenPreferencesAreAbsentOrInvalid(string? json, UiLayoutProfile fallback)
    {
        using var directory = new TempDirectory("sufni-launch-preferences-test");
        var path = Path.Combine(directory.Path, "app-preferences.json");
        if (json is not null)
        {
            await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        }

        var launch = AppPreferences.ReadLaunchValues(path, fallback);

        Assert.Equal(fallback, launch.LayoutProfile);
        Assert.Equal(SufniThemeMode.Dark, launch.ThemeMode);
    }



    [Fact]
    public async Task MapPreferences_PersistsOnlyDeviceSelection()
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var selected = Guid.NewGuid();
        var preferences = TestAppPreferences.Load(path);
        await preferences.Map.SetSelectedLayerIdAsync(selected);

        Assert.Equal(selected, await TestAppPreferences.Load(path).Map.GetSelectedLayerIdAsync());
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.False(json.RootElement.TryGetProperty("updated", out _));
        Assert.False(json.RootElement.GetProperty("maps").TryGetProperty("customLayers", out _));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }


    [Fact]
    public async Task Preferences_UsesLaunchSnapshot_WhenFileChangesBeforeRuntimeConstruction()
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
            """, cancellationToken: TestContext.Current.CancellationToken);
        var launch = AppPreferences.ReadLaunchValues(preferencesPath, UiLayoutProfile.Compact);

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
            """, cancellationToken: TestContext.Current.CancellationToken);

        var preferences = new AppPreferences(preferencesPath, launch, new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher());
        Assert.Equal(initialLayerId, await preferences.Map.GetSelectedLayerIdAsync());

        var reloaded = TestAppPreferences.Load(preferencesPath);
        Assert.Equal(externalLayerId, await reloaded.Map.GetSelectedLayerIdAsync());
    }

    [Fact]
    public async Task MapPreferences_ObserveChanges_ReplaysAndReportsCommittedLocalSelection()
    {
        using var directory = new TempDirectory();
        var preferences = TestAppPreferences.Load(CreatePreferencesPath(directory));
        var observed = new List<Guid?>();
        using var watch = preferences.Map.ObserveChanges().Subscribe(observed.Add);
        var selected = Guid.NewGuid();

        await preferences.Map.SetSelectedLayerIdAsync(selected);
        await preferences.Map.SetSelectedLayerIdAsync(selected);

        Assert.Equal(new Guid?[] { null, selected }, observed);
    }


    [Theory]
    [InlineData(SufniThemeMode.Light, "Light")]
    [InlineData(SufniThemeMode.System, "System")]
    public async Task ThemePreferences_PersistMode_RoundTripsThroughDiskAndJson(
        SufniThemeMode mode,
        string storedMode)
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = CreatePreferencesPath(tempDirectory);

        var preferences = TestAppPreferences.Load(preferencesPath);
        await preferences.Theme.SetModeAsync(mode);

        var reloaded = TestAppPreferences.Load(preferencesPath);
        Assert.Equal(mode, await reloaded.Theme.GetModeAsync());

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(storedMode, json.RootElement.GetProperty("theme").GetProperty("mode").GetString());
    }



    [Theory]
    [InlineData(null, SufniThemeMode.Dark)]
    [InlineData("{\"version\":1,\"theme\":{\"mode\":\"Solarized\"}}", SufniThemeMode.Dark)]
    public async Task ThemePreferences_LoadsDefaultOrFallbackMode(string? document, SufniThemeMode expectedMode)
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = CreatePreferencesPath(tempDirectory);

        if (document is not null)
        {
            await File.WriteAllTextAsync(preferencesPath, document, cancellationToken: TestContext.Current.CancellationToken);
        }

        var preferences = TestAppPreferences.Load(preferencesPath);

        Assert.Equal(expectedMode, await preferences.Theme.GetModeAsync());
    }

    [Fact]
    public async Task UiPreferences_PersistsNextLaunchLayout()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var launch = AppPreferences.ReadLaunchValues(preferencesPath, UiLayoutProfile.Compact);
        var preferences = new AppPreferences(preferencesPath, launch, new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher());

        await preferences.Ui.SetLayoutProfileAsync(UiLayoutProfile.Workspace);

        Assert.Equal(UiLayoutProfile.Compact, launch.LayoutProfile);
        Assert.Equal(UiLayoutProfile.Workspace,
            AppPreferences.ReadLaunchValues(preferencesPath, UiLayoutProfile.Compact).LayoutProfile);
    }

    [Fact]
    public async Task SessionPreferences_ReturnDefaults_WhenFileOrSessionIsMissing()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");

        var preferences = TestAppPreferences.Load(preferencesPath);

        var missingFilePreferences = await preferences.Session.GetRecordedAsync(Guid.NewGuid());
        AssertDefaultSessionPreferences(missingFilePreferences);

        await File.WriteAllTextAsync(preferencesPath, "{\"version\":1}", cancellationToken: TestContext.Current.CancellationToken);
        var missingSessionPreferences = await preferences.Session.GetRecordedAsync(Guid.NewGuid());

        AssertDefaultSessionPreferences(missingSessionPreferences);
    }

    [Theory]
    [InlineData(RecordedPreferencePersistenceCase.SignalDisplayVisibility)]
    [InlineData(RecordedPreferencePersistenceCase.SignalSmoothing)]
    [InlineData(RecordedPreferencePersistenceCase.SignalLayoutHierarchy)]
    [InlineData(RecordedPreferencePersistenceCase.LayoutAndPaneRatios)]
    public async Task SessionPreferences_UpdateRecorded_PersistsPreferenceCase(
        RecordedPreferencePersistenceCase persistenceCase)
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = CreatePreferencesPath(tempDirectory);
        var sessionId = Guid.NewGuid();
        var testCase = CreateRecordedPreferenceCase(persistenceCase);
        var preferences = TestAppPreferences.Load(preferencesPath);

        await preferences.Session.UpdateRecordedAsync(sessionId, testCase.Update);

        var reloaded = TestAppPreferences.Load(preferencesPath);
        var stored = await reloaded.Session.GetRecordedAsync(sessionId);

        testCase.AssertStored(stored);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken: TestContext.Current.CancellationToken));
        testCase.AssertJson(GetRecordedSessionJson(json, sessionId));
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_MergesSessionEntryWithoutDeletingOthers()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();

        var preferences = TestAppPreferences.Load(preferencesPath);

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

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken: TestContext.Current.CancellationToken));
        var sessions = json.RootElement.GetProperty("session").GetProperty("sessions");
        Assert.Equal(2, sessions.EnumerateObject().Count());
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
            """, cancellationToken: TestContext.Current.CancellationToken);
        var preferences = TestAppPreferences.Load(preferencesPath);

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
            """, cancellationToken: TestContext.Current.CancellationToken);
        var preferences = TestAppPreferences.Load(preferencesPath);

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
            """, cancellationToken: TestContext.Current.CancellationToken);
        var preferences = TestAppPreferences.Load(preferencesPath);

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
        var preferences = TestAppPreferences.Load(preferencesPath);

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

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(AppPreferences.CurrentVersion, json.RootElement.GetProperty("version").GetInt32());
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

        var preferences = TestAppPreferences.Load(preferencesPath);

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

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken: TestContext.Current.CancellationToken));
        var sessions = json.RootElement.GetProperty("session").GetProperty("sessions");
        Assert.Equal(sessionIds.Length, sessions.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(RecordedPreferenceFallbackCase.MalformedJson)]
    [InlineData(RecordedPreferenceFallbackCase.UnknownEnumValues)]
    public async Task SessionPreferences_ReturnDefaults_ForInvalidInput(
        RecordedPreferenceFallbackCase fallbackCase)
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = CreatePreferencesPath(tempDirectory);
        var sessionId = Guid.NewGuid();
        var jsonText = fallbackCase switch
        {
            RecordedPreferenceFallbackCase.MalformedJson => "{ not valid json",
            RecordedPreferenceFallbackCase.UnknownEnumValues => CreateUnknownEnumPreferenceJson(sessionId),
            _ => throw new ArgumentOutOfRangeException(nameof(fallbackCase), fallbackCase, null),
        };

        await File.WriteAllTextAsync(preferencesPath, jsonText, cancellationToken: TestContext.Current.CancellationToken);
        var preferences = TestAppPreferences.Load(preferencesPath);

        var stored = await preferences.Session.GetRecordedAsync(sessionId);
        if (fallbackCase == RecordedPreferenceFallbackCase.UnknownEnumValues)
        {
            AssertUnknownEnumFallback(stored);
            return;
        }

        AssertDefaultSessionPreferences(stored);

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Travel = false },
        });

        var updated = await preferences.Session.GetRecordedAsync(sessionId);
        Assert.False(updated.SignalDisplay.Travel);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.TryGetProperty("session", out _));
    }







    [Theory]
    [InlineData(ObserveRecordedValueCase.LocalUpdate)]
    [InlineData(ObserveRecordedValueCase.ReplayCurrentValue)]
    public async Task ObserveRecorded_EmitsExpectedValue(ObserveRecordedValueCase valueCase)
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = CreatePreferencesPath(tempDirectory);
        var sessionId = Guid.NewGuid();
        var preferences = TestAppPreferences.Load(preferencesPath);

        if (valueCase == ObserveRecordedValueCase.ReplayCurrentValue)
        {
            await preferences.Session.UpdateRecordedAsync(sessionId, current =>
                current with
                {
                    Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag },
                });

            var replayed = await preferences.Session.ObserveRecorded(sessionId)
                .FirstAsync()
                .ToTask()
                .AwaitBoundedAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(TravelDistributionMode.DynamicSag, replayed.Analysis.TravelDistributionMode);
            return;
        }

        var initialEmission = new TaskCompletionSource<SessionPreferences>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changedEmission = new TaskCompletionSource<SessionPreferences>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                    changedEmission.TrySetResult(value);
                }
            });

        AssertDefaultSessionPreferences(await initialEmission.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));

        await preferences.Session.UpdateRecordedAsync(sessionId, current => current with
        {
            Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag }
        });

        var observed = await changedEmission.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TravelDistributionMode.DynamicSag, observed.Analysis.TravelDistributionMode);
    }



    [Fact]
    public async Task ObserveRecorded_ReusesObservableAfterLastUnsubscribe_WithoutInterruptingOtherObservers()
    {
        using var directory = new TempDirectory();
        var preferences = TestAppPreferences.Load(CreatePreferencesPath(directory));
        var id = Guid.NewGuid();
        var observable = preferences.Session.ObserveRecorded(id);
        var closed = new List<SessionPreferences>();
        var initial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (observable.Subscribe(value => { closed.Add(value); initial.TrySetResult(); }))
            await initial.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await preferences.Session.UpdateRecordedAsync(id, current => current with
        {
            Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag }
        });
        var reopened = new List<SessionPreferences>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var retained = observable.Subscribe(value => { reopened.Add(value); ready.TrySetResult(); });
        await ready.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        var other = await observable.FirstAsync().ToTask().AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await preferences.Session.RemoveRecordedAsync(id);

        Assert.Single(closed);
        Assert.Equal(TravelDistributionMode.DynamicSag, other.Analysis.TravelDistributionMode);
        Assert.Equal(other, reopened[0]);
        Assert.Equal(SessionPreferences.Default, reopened[1]);
        Assert.Equal(2, reopened.Count);
    }

    [Fact]
    public async Task ObserveRecorded_DistinctUntilChanged_SquashesIdenticalRefresh()
    {
        using var tempDirectory = new TempDirectory("sufni-preferences-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var sessionId = Guid.NewGuid();
        var preferences = TestAppPreferences.Load(preferencesPath);

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
        await initialEmission.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var stored = SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with
            {
                TravelDistributionMode = TravelDistributionMode.DynamicSag,
            },
        };
        await preferences.Session.UpdateRecordedAsync(sessionId, _ => stored);
        await firstStoredEmission.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await preferences.Session.UpdateRecordedAsync(sessionId, _ => stored);

        await AssertDoesNotCompleteAsync(duplicateEmission.Task, TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, emissions);
    }

    private static async Task AssertDoesNotCompleteAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout, TestContext.Current.CancellationToken));
        Assert.NotSame(task, completed);
    }

    private static string CreatePreferencesPath(TempDirectory tempDirectory)
    {
        return Path.Combine(tempDirectory.Path, "app-preferences.json");
    }

    private static RecordedPreferenceCase CreateRecordedPreferenceCase(
        RecordedPreferencePersistenceCase persistenceCase)
    {
        return persistenceCase switch
        {
            RecordedPreferencePersistenceCase.SignalDisplayVisibility => new RecordedPreferenceCase(
                current => current with
                {
                    SignalDisplay = current.SignalDisplay with
                    {
                        Travel = false,
                        Velocity = true,
                        Imu = false,
                        Speed = true,
                        Elevation = false,
                    },
                },
                stored =>
                {
                    Assert.False(stored.SignalDisplay.Travel);
                    Assert.True(stored.SignalDisplay.Velocity);
                    Assert.False(stored.SignalDisplay.Imu);
                    Assert.True(stored.SignalDisplay.Speed);
                    Assert.False(stored.SignalDisplay.Elevation);
                },
                session =>
                {
                    var signalDisplay = session.GetProperty("signalDisplay");
                    Assert.False(signalDisplay.GetProperty("travel").GetBoolean());
                    Assert.True(signalDisplay.GetProperty("velocity").GetBoolean());
                    Assert.False(signalDisplay.GetProperty("imu").GetBoolean());
                    Assert.True(signalDisplay.GetProperty("speed").GetBoolean());
                    Assert.False(signalDisplay.GetProperty("elevation").GetBoolean());
                    Assert.False(session.TryGetProperty("plots", out _));
                }),

            RecordedPreferencePersistenceCase.SignalSmoothing => new RecordedPreferenceCase(
                current => current with
                {
                    SignalDisplay = current.SignalDisplay with
                    {
                        TravelSmoothing = PlotSmoothingLevel.Light,
                        VelocitySmoothing = PlotSmoothingLevel.Strong,
                        ImuSmoothing = PlotSmoothingLevel.Off,
                        SpeedSmoothing = PlotSmoothingLevel.Light,
                        ElevationSmoothing = PlotSmoothingLevel.Strong,
                    },
                },
                stored =>
                {
                    Assert.Equal(PlotSmoothingLevel.Light, stored.SignalDisplay.TravelSmoothing);
                    Assert.Equal(PlotSmoothingLevel.Strong, stored.SignalDisplay.VelocitySmoothing);
                    Assert.Equal(PlotSmoothingLevel.Off, stored.SignalDisplay.ImuSmoothing);
                    Assert.Equal(PlotSmoothingLevel.Light, stored.SignalDisplay.SpeedSmoothing);
                    Assert.Equal(PlotSmoothingLevel.Strong, stored.SignalDisplay.ElevationSmoothing);
                },
                session =>
                {
                    var signalDisplay = session.GetProperty("signalDisplay");
                    Assert.Equal("Light", signalDisplay.GetProperty("travelSmoothing").GetString());
                    Assert.Equal("Strong", signalDisplay.GetProperty("velocitySmoothing").GetString());
                    Assert.Equal("Off", signalDisplay.GetProperty("imuSmoothing").GetString());
                    Assert.Equal("Light", signalDisplay.GetProperty("speedSmoothing").GetString());
                    Assert.Equal("Strong", signalDisplay.GetProperty("elevationSmoothing").GetString());
                    Assert.False(session.TryGetProperty("plots", out _));
                }),

            RecordedPreferencePersistenceCase.SignalLayoutHierarchy => CreateSignalLayoutHierarchyCase(),
            RecordedPreferencePersistenceCase.LayoutAndPaneRatios => CreateLayoutAndPaneRatioCase(),
            _ => throw new ArgumentOutOfRangeException(nameof(persistenceCase), persistenceCase, null),
        };
    }

    private static RecordedPreferenceCase CreateSignalLayoutHierarchyCase()
    {
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

        return new RecordedPreferenceCase(
            current => current with { SignalLayout = signalLayout },
            stored => Assert.Equal(signalLayout, stored.SignalLayout),
            session =>
            {
                Assert.False(session.TryGetProperty("graph", out _));
                var rows = session
                    .GetProperty("signalLayout")
                    .GetProperty("rows");

                Assert.Equal(SignalRowIds.Imu, rows[0].GetProperty("rowId").GetString());
                Assert.False(rows[0].GetProperty("isExpanded").GetBoolean());
                Assert.Equal(SignalRowIds.Velocity, rows[0].GetProperty("children")[0].GetProperty("rowId").GetString());
                Assert.Equal(SignalRowIds.Travel, rows[1].GetProperty("rowId").GetString());
                Assert.False(rows[1].GetProperty("children")[0].GetProperty("isExpanded").GetBoolean());
            });
    }

    private static RecordedPreferenceCase CreateLayoutAndPaneRatioCase()
    {
        var signalLayout = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(SignalRowIds.Travel, heightRatio: 0.25),
            new SignalLayoutRowPreferences(SignalRowIds.Imu, heightRatio: 0.75),
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
                new SessionPaneSizePreference(SessionLayoutPaneIds.Video, 0.55),
            ]));

        return new RecordedPreferenceCase(
            current => current with
            {
                SignalLayout = signalLayout,
                Layout = layout,
            },
            stored =>
            {
                Assert.Equal(signalLayout, stored.SignalLayout);
                Assert.Equal(layout, stored.Layout);
            },
            session =>
            {
                Assert.False(session.TryGetProperty("graph", out _));
                Assert.Equal(
                    0.25,
                    session.GetProperty("signalLayout").GetProperty("rows")[0].GetProperty("heightRatio").GetDouble());

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
                    SessionLayoutPaneIds.Video,
                    layoutJson.GetProperty("desktopMediaRows").GetProperty("panes")[1].GetProperty("paneId").GetString());
            });
    }

    private static JsonElement GetRecordedSessionJson(JsonDocument json, Guid sessionId)
    {
        return json.RootElement
            .GetProperty("session")
            .GetProperty("sessions")
            .GetProperty(sessionId.ToString("D"));
    }

    private static string CreateUnknownEnumPreferenceJson(Guid sessionId)
    {
        return $$"""
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
                """;
    }

    private static void AssertUnknownEnumFallback(SessionPreferences stored)
    {
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

    private sealed record RecordedPreferenceCase(
        Func<SessionPreferences, SessionPreferences> Update,
        Action<SessionPreferences> AssertStored,
        Action<JsonElement> AssertJson);

}
