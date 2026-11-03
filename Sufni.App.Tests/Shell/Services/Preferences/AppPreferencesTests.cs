using Sufni.App.Shell.Services.Preferences;
using Sufni.App.Shared.Plots;
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
namespace Sufni.App.Tests.Shell.Services.Preferences;

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

    [Theory]
    [InlineData(null, UiLayoutProfile.Compact)]
    [InlineData("{broken", UiLayoutProfile.Workspace)]
    [InlineData("{}", UiLayoutProfile.Compact)]
    [InlineData("{\"theme\":{\"mode\":\"Solarized\"}}", UiLayoutProfile.Workspace)]
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

    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("{\"session\":{\"sessions\":{}}}")]
    public async Task MissingOrMalformedSessionPreferences_CanBeUpdated(string? json)
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        if (json is not null) await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        var preferences = TestAppPreferences.Load(path);
        var id = Guid.NewGuid();
        Assert.True((await preferences.Session.GetRecordedAsync(id)).SignalDisplay.Travel);

        await preferences.Session.UpdateRecordedAsync(id, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Travel = false }
        });

        Assert.False((await TestAppPreferences.Load(path).Session.GetRecordedAsync(id)).SignalDisplay.Travel);
    }

    [Theory]
    [InlineData("\"plots\":{\"travel\":false,\"travelSmoothing\":\"Strong\"}", false, PlotSmoothingLevel.Strong)]
    [InlineData("\"plots\":{\"travel\":true,\"travelSmoothing\":\"Strong\"},\"signalDisplay\":{\"travel\":false,\"travelSmoothing\":\"Light\"}", false, PlotSmoothingLevel.Light)]
    [InlineData("\"signalDisplay\":{\"travel\":false,\"travelSmoothing\":\"Unknown\"}", false, PlotSmoothingLevel.Off)]
    public async Task SignalDisplay_ReadsLegacyKeys_AndPrefersCurrentKeys(string fields, bool visible, PlotSmoothingLevel smoothing)
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(path, "{\"session\":{\"sessions\":{\"" + id.ToString("D") + "\":{" + fields + "}}}}", TestContext.Current.CancellationToken);
        var preferences = TestAppPreferences.Load(path);

        var stored = await preferences.Session.GetRecordedAsync(id);

        Assert.Equal(visible, stored.SignalDisplay.Travel);
        Assert.Equal(smoothing, stored.SignalDisplay.TravelSmoothing);
        await preferences.Session.UpdateRecordedAsync(id, current => current);
        var reloaded = await TestAppPreferences.Load(path).Session.GetRecordedAsync(id);
        Assert.Equal(stored.SignalDisplay, reloaded.SignalDisplay);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var entry = saved.RootElement.GetProperty("session").GetProperty("sessions").GetProperty(id.ToString("D"));
        Assert.False(entry.TryGetProperty("plots", out _));
    }

    [Theory]
    [InlineData("\"statistics\":{\"travelHistogramMode\":\"DynamicSag\"}", TravelDistributionMode.DynamicSag)]
    [InlineData("\"statistics\":{\"travelHistogramMode\":\"DynamicSag\"},\"analysis\":{\"travelDistributionMode\":\"ActiveSuspension\"}", TravelDistributionMode.ActiveSuspension)]
    [InlineData("\"analysis\":{\"travelDistributionMode\":\"Unknown\"}", TravelDistributionMode.ActiveSuspension)]
    public async Task Analysis_ReadsLegacyKeys_AndPrefersCurrentKeys(string fields, TravelDistributionMode expected)
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(path, "{\"session\":{\"sessions\":{\"" + id.ToString("D") + "\":{" + fields + "}}}}", TestContext.Current.CancellationToken);

        var stored = await TestAppPreferences.Load(path).Session.GetRecordedAsync(id);

        Assert.Equal(expected, stored.Analysis.TravelDistributionMode);
    }

    [Theory]
    [InlineData("\"graph\":{\"rows\":[{\"rowId\":\"imu\",\"isExpanded\":false,\"children\":[{\"rowId\":\"velocity\"}]}]}", "imu")]
    [InlineData("\"graph\":{\"rows\":[{\"rowId\":\"imu\"}]},\"signalLayout\":{\"rows\":[{\"rowId\":\"travel\",\"isExpanded\":false,\"children\":[{\"rowId\":\"velocity\"}]}]}", "travel")]
    public async Task SignalHierarchy_ReadsLegacyKeys_AndPreservesTreeWhenSaved(string fields, string rootId)
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(path, "{\"session\":{\"sessions\":{\"" + id.ToString("D") + "\":{" + fields + "}}}}", TestContext.Current.CancellationToken);
        var preferences = TestAppPreferences.Load(path);

        var stored = await preferences.Session.GetRecordedAsync(id);

        Assert.Equal(rootId, stored.SignalLayout.Rows[0].RowId);
        Assert.False(stored.SignalLayout.Rows[0].IsExpanded);
        Assert.Equal("velocity", Assert.Single(stored.SignalLayout.Rows[0].Children).RowId);
        await preferences.Session.UpdateRecordedAsync(id, current => current);
        Assert.Equal(stored.SignalLayout, (await TestAppPreferences.Load(path).Session.GetRecordedAsync(id)).SignalLayout);
    }

    [Fact]
    public async Task PaneLayout_PrefersCurrentPaneAndGroupOverLegacyValues()
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(path, """
            {"session":{"sessions":{"SESSION_ID":{"layout":{
              "desktopGraphMediaColumns":{"panes":[{"paneId":"graph","ratio":0.1}]},
              "desktopSignalsMediaColumns":{"panes":[
                {"paneId":"graph","ratio":0.2},
                {"paneId":"signals","ratio":0.8,"isCollapsed":true},
                {"paneId":"media","ratio":0.2}]}
            }}}}}
            """.Replace("SESSION_ID", id.ToString("D")), TestContext.Current.CancellationToken);

        var stored = await TestAppPreferences.Load(path).Session.GetRecordedAsync(id);

        var panes = stored.Layout.DesktopSignalsMediaColumns!.Panes;
        Assert.Equal(2, panes.Count);
        Assert.Equal("signals", panes[0].PaneId);
        Assert.Equal(0.8, panes[0].Ratio);
        Assert.True(panes[0].IsCollapsed);
    }

    [Fact]
    public async Task ConcurrentSessionUpdates_PreserveBothEdits_AfterPendingCommit()
    {
        using var directory = new TempDirectory();
        var path = CreatePreferencesPath(directory);
        var dispatcher = new ManualUiThreadDispatcher();
        var preferences = new AppPreferences(path, AppPreferences.ReadLaunchValues(path, UiLayoutProfile.Workspace), new InlineBackgroundTaskRunner(), dispatcher);
        var id = Guid.NewGuid();
        var first = preferences.Session.UpdateRecordedAsync(id, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Travel = false }
        });
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        var second = preferences.Session.UpdateRecordedAsync(id, current => current with
        {
            Analysis = current.Analysis with { TravelDistributionMode = TravelDistributionMode.DynamicSag }
        });
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await dispatcher.DrainAsync();
        await first.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        if (!second.IsCompleted)
        {
            await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            await dispatcher.DrainAsync();
        }
        await second.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var stored = await TestAppPreferences.Load(path).Session.GetRecordedAsync(id);
        Assert.False(stored.SignalDisplay.Travel);
        Assert.Equal(TravelDistributionMode.DynamicSag, stored.Analysis.TravelDistributionMode);
    }

    [Fact]
    public async Task ObserveRecorded_ReplaysCurrentValue_AndOmitsRepeatedCommits()
    {
        using var directory = new TempDirectory();
        var preferences = TestAppPreferences.Load(CreatePreferencesPath(directory));
        var id = Guid.NewGuid();
        await preferences.Session.UpdateRecordedAsync(id, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Travel = false }
        });
        var observed = new List<bool>();
        using var subscription = preferences.Session.ObserveRecorded(id).Subscribe(value => observed.Add(value.SignalDisplay.Travel));
        await preferences.Session.UpdateRecordedAsync(id, current => current);
        await preferences.Session.UpdateRecordedAsync(id, current => current with
        {
            SignalDisplay = current.SignalDisplay with { Travel = true }
        });

        Assert.Equal([false, true], observed);
    }

    private static string CreatePreferencesPath(TempDirectory directory) =>
        Path.Combine(directory.Path, "app-preferences.json");
}
