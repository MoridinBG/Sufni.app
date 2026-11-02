using NSubstitute;

using Sufni.App.Acquisition.Models;
namespace Sufni.App.Tests.TestSupport.Fixtures;

public static class TestTelemetrySources
{
    public static ITelemetryDataStore CreateDataStore(string name = "store", Guid? boardId = null)
    {
        var dataStore = Substitute.For<ITelemetryDataStore>();
        dataStore.Name.Returns(name);
        dataStore.BoardId.Returns(boardId);
        return dataStore;
    }

    public static SessionImportItem CreateImportItem(
        string name = "file", string description = "", DateTime? startTime = null,
        string duration = "1s", ImportFileAction action = ImportFileAction.Import,
        string? malformedMessage = null, bool? canImport = null,
        byte[]? sourceBytes = null, TelemetryFileSource? source = null) =>
        new(CreateTelemetryFile(name, startTime, duration, malformedMessage, canImport, sourceBytes, source),
            name, description, action);

    public static ITelemetryFile CreateTelemetryFile(
        string name = "file",
        DateTime? startTime = null,
        string duration = "1s",
        string? malformedMessage = null,
        bool? canImport = null,
        byte[]? sourceBytes = null,
        TelemetryFileSource? source = null)
    {
        var telemetryFile = Substitute.For<ITelemetryFile>();
        telemetryFile.FileName.Returns($"{name}.SST");
        telemetryFile.StartTime.Returns(startTime ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        telemetryFile.Duration.Returns(duration);
        telemetryFile.MalformedMessage.Returns(malformedMessage);
        telemetryFile.CanImport.Returns(canImport ?? string.IsNullOrWhiteSpace(malformedMessage));
        telemetryFile.ReadSourceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(source ?? new TelemetryFileSource($"{name}.SST", sourceBytes ?? [1, 2, 3])));
        return telemetryFile;
    }
}
