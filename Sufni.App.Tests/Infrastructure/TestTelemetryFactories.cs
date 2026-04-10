using NSubstitute;
using Sufni.App.Models;

namespace Sufni.App.Tests.Infrastructure;

public static class TestTelemetryFactories
{
    public static ITelemetryDataStore CreateDataStore(string name = "store", Guid? boardId = null)
    {
        var dataStore = Substitute.For<ITelemetryDataStore>();
        dataStore.Name.Returns(name);
        dataStore.BoardId.Returns(boardId);
        return dataStore;
    }

    public static ITelemetryFile CreateTelemetryFile(
        string name = "file",
        string description = "",
        DateTime? startTime = null,
        string duration = "1s",
        bool? shouldBeImported = true,
        bool malformed = false,
        string? malformedMessage = null,
        bool hasUnknown = false)
    {
        var telemetryFile = Substitute.For<ITelemetryFile>();
        telemetryFile.Name.Returns(name);
        telemetryFile.FileName.Returns($"{name}.SST");
        telemetryFile.Description.Returns(description);
        telemetryFile.StartTime.Returns(startTime ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        telemetryFile.Duration.Returns(duration);
        telemetryFile.ShouldBeImported.Returns(shouldBeImported);
        telemetryFile.Malformed.Returns(malformed);
        telemetryFile.MalformedMessage.Returns(malformedMessage);
        telemetryFile.HasUnknown.Returns(hasUnknown);
        return telemetryFile;
    }
}