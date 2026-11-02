using Sufni.App.Tests.TestSupport.Acquisition;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.Acquisition.Models;

public class NetworkTelemetryDataStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFiles_WhenPortIsBusy_ReturnsHolderWithoutIdentifyingOrListing(bool advertisedIdentity)
    {
        var harness = new NetworkTelemetrySourceHarness();
        if (!advertisedIdentity) harness.Entries.OnNext([harness.Entries.Value.Single() with { BoardId = null }]);
        var holder = new DaqTransportHolder.Streaming();
        harness.Registry.AcquireIdentifyAsync("daq").Returns(Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Busy(holder)));
        harness.Registry.AcquireManagementAsync("daq", DaqManagementOperation.ListFiles)
            .Returns(Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Busy(holder)));

        var result = await harness.CreateStore().GetFiles(TestContext.Current.CancellationToken);

        Assert.Equal(holder, Assert.IsType<TelemetryFileListing.Busy>(result).Holder);
        await harness.Inspector.DidNotReceive().InspectAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
        await harness.Management.DidNotReceive().ListDirectoryAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemovedCatalogEntry_BlocksListingAndImportWithoutOpeningAConnection()
    {
        var harness = new NetworkTelemetrySourceHarness();
        var store = harness.CreateStore();
        harness.Entries.OnNext([]);

        Assert.IsType<TelemetryFileListing.Unavailable>(await store.GetFiles(TestContext.Current.CancellationToken));
        Assert.IsType<TelemetryImportBatchAcquisition.Unavailable>(await store.OpenImportBatchAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        await harness.Management.DidNotReceive().ListDirectoryAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
        await harness.Registry.DidNotReceive().AcquireImportAsync(Arg.Any<string>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task GetFiles_RejectsIdentityResultForRemovedCatalogEntry()
    {
        var harness = new NetworkTelemetrySourceHarness();
        harness.Entries.OnNext([harness.Entries.Value.Single() with { BoardId = null }]);
        var identity = new TaskCompletionSource<BoardIdInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Inspector.InspectAsync(harness.Endpoint, Arg.Any<CancellationToken>()).Returns(identity.Task);
        var request = harness.CreateStore().GetFiles(TestContext.Current.CancellationToken);
        harness.Entries.OnNext([]);
        identity.SetResult(new BoardIdInspection.Found(harness.BoardId));

        Assert.IsType<TelemetryFileListing.Unavailable>(await request.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        await harness.Management.DidNotReceive().ListDirectoryAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFiles_CancellationReleasesIdentifyReservation()
    {
        var harness = new NetworkTelemetrySourceHarness();
        harness.Entries.OnNext([harness.Entries.Value.Single() with { BoardId = null }]);
        var reservation = Substitute.For<IDisposable>();
        harness.Registry.AcquireIdentifyAsync("daq").Returns(Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Acquired(reservation)));
        harness.Inspector.InspectAsync(harness.Endpoint, Arg.Any<CancellationToken>()).Returns(call =>
            new TaskCompletionSource<BoardIdInspection>().Task.WaitAsync(call.Arg<CancellationToken>()));
        using var cancellation = new CancellationTokenSource();
        var request = harness.CreateStore().GetFiles(cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));

        reservation.Received(1).Dispose();
        await harness.Management.DidNotReceive().ListDirectoryAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenImportBatchAsync_WhenPortIsBusy_ReturnsHolder()
    {
        var harness = new NetworkTelemetrySourceHarness();
        var holder = new DaqTransportHolder.Management(DaqManagementOperation.SetTime);
        harness.Registry.AcquireImportAsync("daq", Arg.Any<Guid>()).Returns(Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Busy(holder)));

        var acquisition = await harness.CreateStore().OpenImportBatchAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(holder, Assert.IsType<TelemetryImportBatchAcquisition.Busy>(acquisition).Holder);
        await harness.Management.DidNotReceive().OpenSessionAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportBatch_DisposalKeepsReservationUntilManagementSessionCloses()
    {
        var harness = new NetworkTelemetrySourceHarness();
        harness.SetDirectory(new DaqSstFileRecord(DaqFileClass.RootSst, "ride.SST", 100, 1,
            DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(1), 4));
        var session = Substitute.For<IDaqManagementSession>();
        session.TrashFileAsync(1, Arg.Any<CancellationToken>()).Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        harness.Management.OpenSessionAsync(harness.Endpoint, Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));
        var reservation = Substitute.For<IDisposable>();
        harness.Registry.AcquireImportAsync("daq", Arg.Any<Guid>()).Returns(Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Acquired(reservation)));
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.DisposeAsync().Returns(_ => { closing.SetResult(); return new ValueTask(release.Task); });
        var store = harness.CreateStore();
        var files = Assert.IsType<TelemetryFileListing.Listed>(await store.GetFiles(TestContext.Current.CancellationToken)).Files;
        var acquisition = Assert.IsType<TelemetryImportBatchAcquisition.Acquired>(await store.OpenImportBatchAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
        await files[0].OnTrashed();

        var disposal = acquisition.Lifetime!.DisposeAsync().AsTask();
        await closing.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(disposal.IsCompleted);
        reservation.DidNotReceive().Dispose();
        release.SetResult();
        await disposal.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        reservation.Received(1).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenImportBatchAsync_SharesSessionAndRetriesAFileAfterFailedOpen(bool firstOpenFails)
    {
        var endpoint = new ServiceEndpoint(IPAddress.Loopback.ToString(), 5555);
        var management = Substitute.For<IDaqManagementService>();
        var session = Substitute.For<IDaqManagementSession>();
        session.TrashFileAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        var openCount = 0;
        management.OpenSessionAsync(endpoint, Arg.Any<CancellationToken>()).Returns(_ =>
            firstOpenFails && ++openCount == 1
                ? Task.FromException<IDaqManagementSession>(new IOException("connection unavailable"))
                : Task.FromResult(session));
        management.ListDirectoryAsync(endpoint, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<DaqListDirectoryResult>(new DaqListDirectoryResult.Listed(new DaqRootDirectoryRecord([
                new DaqSstFileRecord(DaqFileClass.RootSst, "first.SST", 100, 1, DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(1), 4),
                new DaqSstFileRecord(DaqFileClass.RootSst, "second.SST", 100, 2, DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(1), 4)]))));
        var store = new NetworkTelemetrySourceHarness(management).CreateStore();
        var files = Assert.IsType<TelemetryFileListing.Listed>(await store.GetFiles(TestContext.Current.CancellationToken)).Files;

        await using (Assert.IsType<TelemetryImportBatchAcquisition.Acquired>(
            await store.OpenImportBatchAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Lifetime)
        {
            await management.DidNotReceive().OpenSessionAsync(endpoint, Arg.Any<CancellationToken>());
            if (firstOpenFails) await Assert.ThrowsAsync<IOException>(files[0].OnTrashed);
            else await files[0].OnTrashed();
            await files[1].OnTrashed();
        }

        await session.Received(1).TrashFileAsync(2, Arg.Any<CancellationToken>());
        await management.Received(firstOpenFails ? 2 : 1).OpenSessionAsync(endpoint, Arg.Any<CancellationToken>());
        await session.Received(1).DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFiles_UsesAdvertisedIdentityAndInspectsOnlyWhenItIsAbsent(bool advertised)
    {
        var harness = new NetworkTelemetrySourceHarness();
        if (!advertised) harness.Entries.OnNext([harness.Entries.Value.Single() with { BoardId = null }]);
        var store = harness.CreateStore();

        var listed = Assert.IsType<TelemetryFileListing.Listed>(await store.GetFiles(TestContext.Current.CancellationToken));

        Assert.Equal(harness.BoardId, listed.BoardId);
        await harness.Inspector.Received(advertised ? 0 : 1).InspectAsync(harness.Endpoint, Arg.Any<CancellationToken>());
        await harness.Registry.Received(advertised ? 0 : 1).AcquireIdentifyAsync("daq");
        await harness.Registry.Received(1).AcquireManagementAsync("daq", DaqManagementOperation.ListFiles);
    }

    [Fact]
    public async Task GetFiles_IgnoresConfig_MapsSstMetadata_AndSortsDescending()
    {
        var boardIdInspector = CreateBoardIdInspector();
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .ListDirectoryAsync(new ServiceEndpoint(IPAddress.Loopback.ToString(), 5555), Arg.Any<CancellationToken>())
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

        var dataStore = new NetworkTelemetrySourceHarness(daqManagementService, boardIdInspector).CreateStore();

        var files = Assert.IsType<TelemetryFileListing.Listed>(await dataStore.GetFiles(TestContext.Current.CancellationToken)).Files;

        Assert.Collection(
            files,
            first =>
            {
                Assert.Equal("RIDE-NEW.SST", first.FileName);

                Assert.Equal("00:00:06", first.Duration);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(222).UtcDateTime, first.StartTime);
            },
            second =>
            {
                Assert.Equal("RIDE-OLD.SST", second.FileName);

                Assert.Equal("00:00:03", second.Duration);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(111).UtcDateTime, second.StartTime);
            });
    }

    [Fact]
    public async Task GetFiles_ThrowsWhenListDirectoryReturnsTypedError()
    {
        var boardIdInspector = CreateBoardIdInspector();
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .ListDirectoryAsync(new ServiceEndpoint(IPAddress.Loopback.ToString(), 5555), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqListDirectoryResult>(
                new DaqListDirectoryResult.Error(DaqManagementErrorCode.Busy, "Device busy")));

        var dataStore = new NetworkTelemetrySourceHarness(daqManagementService, boardIdInspector).CreateStore();

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() => dataStore.GetFiles());

        Assert.Equal(DaqManagementErrorCode.Busy, exception.ErrorCode);
    }

    [Fact]
    public async Task GetFiles_MapsMalformedSstMetadata_WithoutFailingTheWholeList()
    {
        var boardIdInspector = CreateBoardIdInspector();
        var daqManagementService = Substitute.For<IDaqManagementService>();
        daqManagementService
            .ListDirectoryAsync(new ServiceEndpoint(IPAddress.Loopback.ToString(), 5555), Arg.Any<CancellationToken>())
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

        var dataStore = new NetworkTelemetrySourceHarness(daqManagementService, boardIdInspector).CreateStore();

        var files = Assert.IsType<TelemetryFileListing.Listed>(await dataStore.GetFiles(TestContext.Current.CancellationToken)).Files;

        Assert.Collection(
            files,
            first =>
            {
                Assert.Equal("RIDE-NEW.SST", first.FileName);

                Assert.Null(first.MalformedMessage);
            },
            second =>
            {
                Assert.Equal("00012.SST", second.FileName);

                Assert.Equal("00:00:06", second.Duration);
                Assert.Equal(DateTimeOffset.UnixEpoch.UtcDateTime, second.StartTime);
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
        var session = Substitute.For<IDaqManagementSession>();
        var sourceBytes = TestSstFiles.CreateValidV3();
        session
            .GetFileAsync(DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var destination = callInfo.ArgAt<Stream>(2);
                destination.Write(sourceBytes);
                return Task.FromResult<DaqGetFileResult>(new DaqGetFileResult.Downloaded("DEVICE.SST", (ulong)sourceBytes.Length));
            });
        session
            .MarkSstUploadedAsync(42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        session
            .TrashFileAsync(42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        var file = CreateNetworkFile(session);

        switch (operation)
        {
            case NetworkTelemetryFileOperation.ReadSource:
            {
                using var source = await file.ReadSourceAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.Equal("DEVICE.SST", source.FileName);
                Assert.Equal(sourceBytes, source.SstBytes.ToArray());
                await session.Received(1)
                    .GetFileAsync(DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
                break;
            }
            case NetworkTelemetryFileOperation.MarkImported:
                await file.OnImported();
                await session.Received(1)
                    .MarkSstUploadedAsync(42, Arg.Any<CancellationToken>());
                break;
            case NetworkTelemetryFileOperation.Trash:
                await file.OnTrashed();
                await session.Received(1)
                    .TrashFileAsync(42, Arg.Any<CancellationToken>());
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
        var session = Substitute.For<IDaqManagementSession>();
        session
            .GetFileAsync(DaqFileClass.RootSst, 42, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqGetFileResult>(
                new DaqGetFileResult.Error(expectedError, "Device rejected request")));
        session
            .MarkSstUploadedAsync(42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(
                new DaqManagementResult.Error(expectedError, "Device rejected request")));
        session
            .TrashFileAsync(42, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(
                new DaqManagementResult.Error(expectedError, "Device rejected request")));
        var file = CreateNetworkFile(session);

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() => operation switch
        {
            NetworkTelemetryFileOperation.ReadSource => file.ReadSourceAsync(cancellationToken: TestContext.Current.CancellationToken),
            NetworkTelemetryFileOperation.MarkImported => file.OnImported(),
            NetworkTelemetryFileOperation.Trash => file.OnTrashed(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        });

        Assert.Equal(expectedError, exception.ErrorCode);
    }

    private static ILiveDaqBoardIdInspector CreateBoardIdInspector()
    {
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        boardIdInspector
            .InspectAsync(new ServiceEndpoint(IPAddress.Loopback.ToString(), 5555), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardIdInspection>(new BoardIdInspection.Found(Guid.NewGuid())));
        return boardIdInspector;
    }

    private static NetworkTelemetryFile CreateNetworkFile(IDaqManagementSession session)
    {
        var file = new NetworkTelemetryFile(
            _ => Task.FromResult(session),
            42,
            "NOT-A-NUMERIC-NAME.SST",
            DateTimeOffset.FromUnixTimeSeconds(111),
            TimeSpan.FromSeconds(6));
        return file;
    }

    public enum NetworkTelemetryFileOperation
    {
        ReadSource,
        MarkImported,
        Trash
    }
}
