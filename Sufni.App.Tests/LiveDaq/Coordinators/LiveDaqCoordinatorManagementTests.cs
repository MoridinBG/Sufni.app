using System.Reactive.Disposables;
using System.Reactive.Subjects;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Models;

namespace Sufni.App.Tests.LiveDaq.Coordinators;

public partial class LiveDaqCoordinatorTests
{
    [Theory]
    [InlineData(DaqManagementOperation.SetTime)]
    [InlineData(DaqManagementOperation.ReadConfig)]
    [InlineData(DaqManagementOperation.WriteConfig)]
    public async Task Management_BusyPortDoesNotCallDeviceService(DaqManagementOperation operation)
    {
        catalogEntries.OnNext([ManagementDevice()]);
        var holder = new DaqTransportHolder.Import(Guid.NewGuid());
        streamRegistry.AcquireManagementAsync("board", operation).Returns(new DaqTransportAcquisition.Busy(holder));
        await using var coordinator = CreateCoordinator();

        var result = await RunManagementAsync(coordinator, operation);

        Assert.Equal(holder, Assert.IsType<LiveDaqManagementResult.Busy>(result).Holder);
        Assert.Empty(managementService.ReceivedCalls());
    }

    [Theory]
    [InlineData(DaqManagementOperation.SetTime, DaqManagementFailureReason.SetTime)]
    [InlineData(DaqManagementOperation.ReadConfig, DaqManagementFailureReason.ReadConfig)]
    [InlineData(DaqManagementOperation.WriteConfig, DaqManagementFailureReason.WriteConfig)]
    public async Task Management_TransportFailureReleasesReservationAndReturnsTypedDiagnostics(
        DaqManagementOperation operation, DaqManagementFailureReason reason)
    {
        catalogEntries.OnNext([ManagementDevice()]);
        var released = false;
        streamRegistry.AcquireManagementAsync("board", operation).Returns(
            new DaqTransportAcquisition.Acquired(Disposable.Create(() => released = true)));
        managementService.SetTimeAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("transport diagnostic"));
        managementService.ReadConfigAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("transport diagnostic"));
        managementService.WriteConfigAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("transport diagnostic"));
        await using var coordinator = CreateCoordinator();

        var result = Assert.IsType<LiveDaqManagementResult.Failed>(await RunManagementAsync(coordinator, operation));

        Assert.Equal(reason, result.Reason);
        Assert.Equal("transport diagnostic", result.Details);
        Assert.True(released);
    }

    [Fact]
    public async Task Management_UsesCurrentCatalogEndpoint_AndReportsUnsupportedOrOfflineWithoutAcquiring()
    {
        await using var coordinator = CreateCoordinator();
        catalogEntries.OnNext([ManagementDevice() with { ProtocolVersion = LiveProtocolVersion.V3 }]);
        Assert.Equal(DaqManagementUnavailability.Unsupported,
            Assert.IsType<LiveDaqManagementResult.Unavailable>(await coordinator.SetTimeAsync("board")).Reason);
        catalogEntries.OnNext([]);
        Assert.Equal(DaqManagementUnavailability.Offline,
            Assert.IsType<LiveDaqManagementResult.Unavailable>(await coordinator.SetTimeAsync("board")).Reason);
        Assert.Empty(streamRegistry.ReceivedCalls());
        catalogEntries.OnNext([ManagementDevice() with { NetworkEndpoint = new ServiceEndpoint("192.168.0.20", 2444) }]);
        streamRegistry.AcquireManagementAsync("board", DaqManagementOperation.SetTime)
            .Returns(new DaqTransportAcquisition.Acquired(Disposable.Empty));
        managementService.SetTimeAsync(new ServiceEndpoint("192.168.0.20", 2444), Arg.Any<CancellationToken>())
            .Returns(new DaqSetTimeResult.Ok(TimeSpan.FromMilliseconds(12)));

        var result = Assert.IsType<LiveDaqManagementResult.TimeSet>(await coordinator.SetTimeAsync("board", TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(12), result.RoundTripTime);
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndDrainsManagementBeforeReleasingItsReservation()
    {
        catalogEntries.OnNext([ManagementDevice()]);
        var released = false;
        streamRegistry.AcquireManagementAsync("board", DaqManagementOperation.SetTime).Returns(
            new DaqTransportAcquisition.Acquired(Disposable.Create(() => released = true)));
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource<DaqSetTimeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        managementService.SetTimeAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<CancellationToken>().Register(() => cancellationObserved.TrySetResult());
            return cleanup.Task;
        });
        var coordinator = CreateCoordinator();
        var command = coordinator.SetTimeAsync("board", TestContext.Current.CancellationToken);

        var disposal = coordinator.DisposeAsync().AsTask();
        await cancellationObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(disposal.IsCompleted);
        Assert.False(released);
        cleanup.SetCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        await disposal.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.True(released);
    }

    [Fact]
    public async Task ManagementAvailability_DerivesPresenceCapabilityAndPortOwnership()
    {
        using var holders = new BehaviorSubject<DaqTransportHolder?>(null);
        streamRegistry.ObserveTransport("board").Returns(holders);
        await using var coordinator = CreateCoordinator();
        LiveDaqManagementAvailability? current = null;
        using var observation = coordinator.ObserveManagementAvailability("board").Subscribe(state => current = state);
        Assert.False(current!.CanManage);
        catalogEntries.OnNext([ManagementDevice()]);
        Assert.True(current.CanManage);
        holders.OnNext(new DaqTransportHolder.Streaming());
        Assert.False(current.CanManage);
        Assert.False(current.CanConnect);
        holders.OnNext(null);
        Assert.True(current.CanManage);
        catalogEntries.OnNext([ManagementDevice() with { ProtocolVersion = LiveProtocolVersion.V3 }]);
        Assert.False(current.CanManage);
        Assert.True(current.CanConnect);
        catalogEntries.OnNext([]);
        Assert.False(current.CanConnect);
    }

    [Fact]
    public async Task SelectConfigFileAsync_RejectsWrongNameAndSeparatesPickerDiagnostics()
    {
        await using var coordinator = CreateCoordinator();
        filesService.OpenDeviceConfigFileAsync(Arg.Any<CancellationToken>()).Returns(new PickedFile("CONFIG.txt", () => Task.FromResult<Stream>(new MemoryStream([1]))));
        Assert.Equal(DaqManagementFailureReason.InvalidConfigName, Assert.IsType<LiveDaqManagementResult.Failed>(
            await coordinator.SelectConfigFileAsync(TestContext.Current.CancellationToken)).Reason);
        filesService.OpenDeviceConfigFileAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("picker diagnostic"));

        var result = Assert.IsType<LiveDaqManagementResult.Failed>(await coordinator.SelectConfigFileAsync(TestContext.Current.CancellationToken));

        Assert.Equal(DaqManagementFailureReason.SelectConfigFile, result.Reason);
        Assert.Equal("picker diagnostic", result.Details);
    }

    private static LiveDaqCatalogEntry ManagementDevice() => new("board", "DAQ", "board", new ServiceEndpoint("192.168.0.10", 1557));

    private static Task<LiveDaqManagementResult> RunManagementAsync(ILiveDaqCoordinator coordinator, DaqManagementOperation operation) => operation switch
    {
        DaqManagementOperation.SetTime => coordinator.SetTimeAsync("board", TestContext.Current.CancellationToken),
        DaqManagementOperation.ReadConfig => coordinator.ReadConfigAsync("board", TestContext.Current.CancellationToken),
        DaqManagementOperation.WriteConfig => coordinator.WriteConfigAsync("board", new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
