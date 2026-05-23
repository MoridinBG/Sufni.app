using NSubstitute;
using Sufni.App.Models;

namespace Sufni.App.Tests.Infrastructure;

public static class TestTelemetrySources
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
        byte version = 4,
        string? malformedMessage = null,
        bool? canImport = null,
        bool hasUnknown = false,
        byte[]? sourceBytes = null)
    {
        var telemetryFile = Substitute.For<ITelemetryFile>();
        telemetryFile.Name.Returns(name);
        telemetryFile.FileName.Returns($"{name}.SST");
        telemetryFile.Description.Returns(description);
        telemetryFile.Version.Returns(version);
        telemetryFile.StartTime.Returns(startTime ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        telemetryFile.Duration.Returns(duration);
        telemetryFile.ShouldBeImported.Returns(shouldBeImported);
        telemetryFile.MalformedMessage.Returns(malformedMessage);
        telemetryFile.CanImport.Returns(canImport ?? string.IsNullOrWhiteSpace(malformedMessage));
        telemetryFile.HasUnknown.Returns(hasUnknown);
        telemetryFile.ReadSourceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TelemetryFileSource($"{name}.SST", sourceBytes ?? [1, 2, 3])));
        return telemetryFile;
    }
}
