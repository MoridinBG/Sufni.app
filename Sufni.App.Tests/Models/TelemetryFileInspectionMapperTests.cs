using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Models;

public class TelemetryFileInspectionMapperTests
{
    [Fact]
    public void Map_WithV5MissingFinalStatusWarning_RemainsImportable()
    {
        var startTime = new DateTime(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
        var inspection = new ValidSstFileInspection(
            Version: 5,
            StartTime: startTime,
            Duration: TimeSpan.FromSeconds(12),
            TelemetrySampleRate: 100,
            HasUnknown: false,
            MalformedMessage: "SST v5 final status is missing; parsed complete data chunks only.");

        var state = TelemetryFileInspectionMapper.Map(inspection, DateTime.UnixEpoch);

        Assert.False(state.ShouldBeImported);
        Assert.True(state.CanImport);
        Assert.False(state.HasUnknown);
        Assert.Equal(5, state.Version);
        Assert.Equal(startTime, state.StartTime);
        Assert.Equal("00:00:12", state.Duration);
        Assert.Equal("SST v5 final status is missing; parsed complete data chunks only.", state.MalformedMessage);
    }

    [Fact]
    public void Map_WithV5UnsupportedTravel_IsNonImportable()
    {
        var startTime = new DateTime(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
        var inspection = new MalformedSstFileInspection(
            Version: 5,
            StartTime: startTime,
            Duration: TimeSpan.FromSeconds(3),
            TelemetrySampleRate: 100,
            Message: "SST v5 travel data is missing or unsupported by this app.");

        var state = TelemetryFileInspectionMapper.Map(inspection, DateTime.UnixEpoch);

        Assert.False(state.ShouldBeImported);
        Assert.False(state.CanImport);
        Assert.False(state.HasUnknown);
        Assert.Equal(5, state.Version);
        Assert.Equal(startTime, state.StartTime);
        Assert.Equal("00:00:03", state.Duration);
        Assert.Equal("SST v5 travel data is missing or unsupported by this app.", state.MalformedMessage);
    }
}
