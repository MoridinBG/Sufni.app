using System.Text.Json;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services;

public class AppPreferencesTests
{
    [Fact]
    public void Constructor_ExposesPreferenceGroups()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();

        try
        {
            var preferences = new AppPreferences(preferencesPath);

            Assert.NotNull(preferences.Map);
            Assert.NotNull(preferences.Session);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task MapPreferences_PersistSelectedLayerAndCustomLayers_UnderMapsGroup()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();
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

        try
        {
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
            Assert.Empty(Directory.EnumerateFiles(tempDirectory, "*.tmp"));
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task SessionPreferences_ReturnDefaults_WhenFileOrSessionIsMissing()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();

        try
        {
            var preferences = new AppPreferences(preferencesPath);

            var missingFilePreferences = await preferences.Session.GetRecordedAsync(Guid.NewGuid());
            AssertDefaultSessionPreferences(missingFilePreferences);

            await File.WriteAllTextAsync(preferencesPath, "{\"version\":1}");
            var missingSessionPreferences = await preferences.Session.GetRecordedAsync(Guid.NewGuid());

            AssertDefaultSessionPreferences(missingSessionPreferences);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsAllPlotVisibilityValues()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();
        var sessionId = Guid.NewGuid();

        try
        {
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
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_MergesSessionEntryWithoutDeletingOthers()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();

        try
        {
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
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_PersistsPlotSmoothingLevels()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();
        var sessionId = Guid.NewGuid();

        try
        {
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
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task SessionPreferences_UpdateRecorded_SerializesConcurrentUpdates()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();
        var sessionIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        try
        {
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
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task SessionPreferences_ReturnDefaults_ForMalformedJson_AndOverwriteOnNextUpdate()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();
        var sessionId = Guid.NewGuid();

        try
        {
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
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task SessionPreferences_ReturnDefaults_ForUnknownEnumValues()
    {
        var (tempDirectory, preferencesPath) = CreatePreferencesPath();
        var sessionId = Guid.NewGuid();

        try
        {
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
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    private static void AssertDefaultSessionPreferences(SessionPreferences preferences)
    {
        Assert.True(preferences.Plots.Travel);
        Assert.True(preferences.Plots.Velocity);
        Assert.True(preferences.Plots.Imu);
        Assert.True(preferences.Plots.Speed);
        Assert.True(preferences.Plots.Elevation);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.TravelSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.VelocitySmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.ImuSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.SpeedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, preferences.Plots.ElevationSmoothing);
        Assert.Equal(TravelHistogramMode.ActiveSuspension, preferences.Statistics.TravelHistogramMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, preferences.Statistics.VelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Zenith, preferences.Statistics.BalanceDisplacementMode);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, preferences.Statistics.SessionAnalysisTargetProfile);
    }

    private static (string TempDirectory, string PreferencesPath) CreatePreferencesPath()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-preferences-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        return (tempDirectory, Path.Combine(tempDirectory, "app-preferences.json"));
    }

    private static void DeleteTempDirectory(string tempDirectory)
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
