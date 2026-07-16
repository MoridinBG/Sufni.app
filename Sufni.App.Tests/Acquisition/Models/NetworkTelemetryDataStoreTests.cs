using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Acquisition.Models;

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

    [Theory]
    [InlineData(NetworkTelemetryFileOperation.ReadSource)]
    [InlineData(NetworkTelemetryFileOperation.MarkImported)]
    [InlineData(NetworkTelemetryFileOperation.Trash)]
    public async Task NetworkTelemetryFile_RoutesSuccessfulOperationByRecordId(
        NetworkTelemetryFileOperation operation)
    {
        Stream? capturedDestination = null;
        byte[]? capturedDestinationBuffer = null;
        var daqManagementService = Substitute.For<IDaqManagementService>();
        var sourceBytes = TestSstFiles.CreateValidV3();
        daqManagementService
            .GetFileAsync(IPAddress.Loopback.ToString(), 5555, DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var destination = callInfo.ArgAt<Stream>(4);
                capturedDestination = destination;
                destination.Write(sourceBytes);
                capturedDestinationBuffer = ((MemoryStream)destination).GetBuffer();
                return Task.FromResult<DaqGetFileResult>(new DaqGetFileResult.Downloaded("DEVICE.SST", (ulong)sourceBytes.Length));
            });
        daqManagementService
            .MarkSstUploadedAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        daqManagementService
            .TrashFileAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        var file = CreateNetworkFile(daqManagementService);

        switch (operation)
        {
            case NetworkTelemetryFileOperation.ReadSource:
            {
                using var source = await file.ReadSourceAsync();
                Assert.Equal("DEVICE.SST", source.FileName);
                Assert.Equal(sourceBytes, source.SstBytes.ToArray());
                Assert.Equal(sourceBytes.Length, source.LogicalLength);
                Assert.True(source.AllocatedCapacity >= source.LogicalLength);
                Assert.True(MemoryMarshal.TryGetArray(source.SstBytes, out var sourceBuffer));
                Assert.Same(capturedDestinationBuffer, sourceBuffer.Array);
                Assert.IsType<MemoryStream>(capturedDestination);
                await daqManagementService.Received(1)
                    .GetFileAsync(IPAddress.Loopback.ToString(), 5555, DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
                break;
            }
            case NetworkTelemetryFileOperation.MarkImported:
                await file.OnImported();
                Assert.True(file.Imported);
                await daqManagementService.Received(1)
                    .MarkSstUploadedAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>());
                break;
            case NetworkTelemetryFileOperation.Trash:
                await file.OnTrashed();
                await daqManagementService.Received(1)
                    .TrashFileAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    [Theory]
    [InlineData(NetworkTelemetryFileOperation.ReadSource, DaqManagementErrorCode.Busy)]
    [InlineData(NetworkTelemetryFileOperation.MarkImported, DaqManagementErrorCode.NotFound)]
    [InlineData(NetworkTelemetryFileOperation.Trash, DaqManagementErrorCode.Busy)]
    public async Task NetworkTelemetryFile_ThrowsTypedError_ForFailedOperation(
        NetworkTelemetryFileOperation operation,
        DaqManagementErrorCode expectedError)
    {
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .GetFileAsync(IPAddress.Loopback.ToString(), 5555, DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqGetFileResult>(
                new DaqGetFileResult.Error(expectedError, "Device rejected request")));
        daqManagementService
            .MarkSstUploadedAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(
                new DaqManagementResult.Error(expectedError, "Device rejected request")));
        daqManagementService
            .TrashFileAsync(IPAddress.Loopback.ToString(), 5555, 42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(
                new DaqManagementResult.Error(expectedError, "Device rejected request")));
        var file = CreateNetworkFile(daqManagementService);

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() => operation switch
        {
            NetworkTelemetryFileOperation.ReadSource => file.ReadSourceAsync(),
            NetworkTelemetryFileOperation.MarkImported => file.OnImported(),
            NetworkTelemetryFileOperation.Trash => file.OnTrashed(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        });

        Assert.Equal(expectedError, exception.ErrorCode);
        if (operation is NetworkTelemetryFileOperation.MarkImported)
        {
            Assert.False(file.Imported);
        }
    }

    private static ILiveDaqBoardIdInspector CreateBoardIdInspector()
    {
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        boardIdInspector
            .InspectAsync(IPAddress.Loopback, 5555, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        return boardIdInspector;
    }

    private static NetworkTelemetryFile CreateNetworkFile(IDaqManagementService daqManagementService) =>
        new(
            new IPEndPoint(IPAddress.Loopback, 5555),
            daqManagementService,
            42,
            "NOT-A-NUMERIC-NAME.SST",
            3,
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));

    public enum NetworkTelemetryFileOperation
    {
        ReadSource,
        MarkImported,
        Trash
    }
}
