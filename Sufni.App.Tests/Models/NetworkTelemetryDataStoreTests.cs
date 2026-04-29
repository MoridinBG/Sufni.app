using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Models;

public class NetworkTelemetryDataStoreTests
{
    [Fact]
    public async Task Initialization_SetsBoardIdFromInspector()
    {
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        var daqManagementService = Substitute.For<IDaqManagementService>();
        var expectedBoardId = Guid.NewGuid();
        boardIdInspector
            .InspectAsync(IPAddress.Loopback, 5555, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(expectedBoardId));

        var dataStore = new NetworkTelemetryDataStore(IPAddress.Loopback, 5555, daqManagementService, boardIdInspector);

        await dataStore.Initialization;

        Assert.Equal(expectedBoardId, dataStore.BoardId);
    }

    [Fact]
    public async Task GetFiles_IgnoresConfig_MapsSstMetadata_AndSortsDescending()
    {
        var boardIdInspector = CreateBoardIdInspector();
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .ListDirectoryAsync(IPAddress.Loopback.ToString(), 5555, DaqDirectoryId.Root, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqListDirectoryResult>(
                new DaqListDirectoryResult.Listed(
                    new DaqRootDirectoryRecord(
                    [
                        new DaqConfigFileRecord("CONFIG", 48),
                        new DaqSstFileRecord(
                            DaqFileClass.RootSst,
                            "RIDE-OLD.SST",
                            1234,
                            12,
                            DateTimeOffset.FromUnixTimeSeconds(111),
                            TimeSpan.FromSeconds(3),
                            3),
                        new DaqSstFileRecord(
                            DaqFileClass.RootSst,
                            "RIDE-NEW.SST",
                            5678,
                            42,
                            DateTimeOffset.FromUnixTimeSeconds(222),
                            TimeSpan.FromSeconds(6),
                            4)
                    ]))));

        var dataStore = new NetworkTelemetryDataStore(IPAddress.Loopback, 5555, daqManagementService, boardIdInspector);
        await dataStore.Initialization;

        var files = await dataStore.GetFiles();

        Assert.Collection(
            files,
            first =>
            {
                Assert.Equal("RIDE-NEW.SST", first.FileName);
                Assert.False(first.ShouldBeImported);
                Assert.Equal((byte)4, first.Version);
                Assert.Equal("00:00:06", first.Duration);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(222).LocalDateTime, first.StartTime);
            },
            second =>
            {
                Assert.Equal("RIDE-OLD.SST", second.FileName);
                Assert.False(second.ShouldBeImported);
                Assert.Equal((byte)3, second.Version);
                Assert.Equal("00:00:03", second.Duration);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(111).LocalDateTime, second.StartTime);
            });
    }

    [Fact]
    public async Task GetFiles_ThrowsWhenListDirectoryReturnsTypedError()
    {
        var boardIdInspector = CreateBoardIdInspector();
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .ListDirectoryAsync(IPAddress.Loopback.ToString(), 5555, DaqDirectoryId.Root, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqListDirectoryResult>(
                new DaqListDirectoryResult.Error(DaqManagementErrorCode.Busy, "Device busy")));

        var dataStore = new NetworkTelemetryDataStore(IPAddress.Loopback, 5555, daqManagementService, boardIdInspector);

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() => dataStore.GetFiles());

        Assert.Equal(DaqManagementErrorCode.Busy, exception.ErrorCode);
    }

    [Fact]
    public async Task GetFiles_MapsMalformedSstMetadata_WithoutFailingTheWholeList()
    {
        var boardIdInspector = CreateBoardIdInspector();
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .ListDirectoryAsync(IPAddress.Loopback.ToString(), 5555, DaqDirectoryId.Root, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqListDirectoryResult>(
                new DaqListDirectoryResult.Listed(
                    new DaqRootDirectoryRecord(
                    [
                        new DaqMalformedSstFileRecord(
                            DaqFileClass.RootSst,
                            "00012.SST",
                            1234,
                            12,
                            TimestampUtc: null,
                            Duration: TimeSpan.FromSeconds(6),
                            SstVersion: 4,
                            MalformedMessage: "The device reported an invalid SST timestamp (7809639177543108896)."),
                        new DaqSstFileRecord(
                            DaqFileClass.RootSst,
                            "RIDE-NEW.SST",
                            5678,
                            42,
                            DateTimeOffset.FromUnixTimeSeconds(222),
                            TimeSpan.FromSeconds(7),
                            4)
                    ]))));

        var dataStore = new NetworkTelemetryDataStore(IPAddress.Loopback, 5555, daqManagementService, boardIdInspector);
        await dataStore.Initialization;

        var files = await dataStore.GetFiles();

        Assert.Collection(
            files,
            first =>
            {
                Assert.Equal("RIDE-NEW.SST", first.FileName);
                Assert.False(first.ShouldBeImported);
                Assert.Null(first.MalformedMessage);
            },
            second =>
            {
                Assert.Equal("00012.SST", second.FileName);
                Assert.False(second.ShouldBeImported);
                Assert.Equal((byte)4, second.Version);
                Assert.Equal("00:00:06", second.Duration);
                Assert.Equal(DateTimeOffset.UnixEpoch.LocalDateTime, second.StartTime);
                Assert.Contains("invalid SST timestamp", second.MalformedMessage);
            });
    }

    [Fact]
    public async Task GeneratePsstAsync_UsesInjectedRecordIdRatherThanFileName()
    {
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .GetFileAsync(IPAddress.Loopback.ToString(), 5555, DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var bytes = CreateValidV3SstBytes();
                var destination = callInfo.ArgAt<Stream>(4);
                destination.Write(bytes);
                return Task.FromResult<DaqGetFileResult>(new DaqGetFileResult.Downloaded("DEVICE.SST", (ulong)bytes.Length));
            });

        var file = new NetworkTelemetryFile(
            new IPEndPoint(IPAddress.Loopback, 5555),
            daqManagementService,
            42,
            "NOT-A-NUMERIC-NAME.SST",
            3,
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));

        var psst = await file.GeneratePsstAsync(CreateBikeData());

        Assert.NotEmpty(psst);
        await daqManagementService.Received(1)
            .GetFileAsync(IPAddress.Loopback.ToString(), 5555, DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GeneratePsstAsync_ThrowsWhenGetFileReturnsTypedError()
    {
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .GetFileAsync(IPAddress.Loopback.ToString(), 5555, DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqGetFileResult>(
                new DaqGetFileResult.Error(DaqManagementErrorCode.Busy, "Device busy")));

        var file = new NetworkTelemetryFile(
            new IPEndPoint(IPAddress.Loopback, 5555),
            daqManagementService,
            42,
            "NOT-A-NUMERIC-NAME.SST",
            3,
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() => file.GeneratePsstAsync(CreateBikeData()));

        Assert.Equal(DaqManagementErrorCode.Busy, exception.ErrorCode);
    }

    [Fact]
    public async Task OnImported_MarksSstUploadedByRecordId()
    {
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .MarkSstUploadedAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));

        var file = new NetworkTelemetryFile(
            new IPEndPoint(IPAddress.Loopback, 5555),
            daqManagementService,
            42,
            "NOT-A-NUMERIC-NAME.SST",
            3,
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));

        await file.OnImported();

        Assert.True(file.Imported);
        await daqManagementService.Received(1)
            .MarkSstUploadedAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnImported_ThrowsWhenMarkUploadedReturnsTypedError()
    {
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .MarkSstUploadedAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(
                new DaqManagementResult.Error(DaqManagementErrorCode.NotFound, "Missing")));

        var file = new NetworkTelemetryFile(
            new IPEndPoint(IPAddress.Loopback, 5555),
            daqManagementService,
            42,
            "NOT-A-NUMERIC-NAME.SST",
            3,
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() => file.OnImported());

        Assert.Equal(DaqManagementErrorCode.NotFound, exception.ErrorCode);
        Assert.False(file.Imported);
    }

    [Fact]
    public async Task OnTrashed_UsesInjectedRecordIdRatherThanFileName()
    {
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .TrashFileAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));

        var file = new NetworkTelemetryFile(
            new IPEndPoint(IPAddress.Loopback, 5555),
            daqManagementService,
            42,
            "NOT-A-NUMERIC-NAME.SST",
            3,
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));

        await file.OnTrashed();

        await daqManagementService.Received(1)
            .TrashFileAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTrashed_ThrowsWhenTrashReturnsTypedError()
    {
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .TrashFileAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(
                new DaqManagementResult.Error(DaqManagementErrorCode.Busy, "Device busy")));

        var file = new NetworkTelemetryFile(
            new IPEndPoint(IPAddress.Loopback, 5555),
            daqManagementService,
            42,
            "NOT-A-NUMERIC-NAME.SST",
            3,
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() => file.OnTrashed());

        Assert.Equal(DaqManagementErrorCode.Busy, exception.ErrorCode);
    }

    private static ILiveDaqBoardIdInspector CreateBoardIdInspector()
    {
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        boardIdInspector
            .InspectAsync(IPAddress.Loopback, 5555, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        return boardIdInspector;
    }

    private static BikeData CreateBikeData() => new(
        HeadAngle: 65.0,
        FrontMaxTravel: 200.0,
        RearMaxTravel: 200.0,
        FrontMeasurementToTravel: measurement => Math.Clamp((measurement - 1500.0) / 5.0, 0.0, 200.0),
        RearMeasurementToTravel: measurement => Math.Clamp((measurement - 1500.0) / 5.0, 0.0, 200.0));

    private static byte[] CreateValidV3SstBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write((ushort)1000);
        writer.Write((ushort)0);
        writer.Write((long)1_700_000_000);

        for (var index = 0; index < 256; index++)
        {
            var front = 1900.0 + 260.0 * Math.Sin(index * 2.0 * Math.PI / 48.0);
            var rear = 1850.0 + 240.0 * Math.Sin(index * 2.0 * Math.PI / 52.0);
            writer.Write((ushort)Math.Round(front));
            writer.Write((ushort)Math.Round(rear));
        }

        return ms.ToArray();
    }
}