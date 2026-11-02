using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Models;

namespace Sufni.App.LiveDaq.Coordinators;

public partial class LiveDaqCoordinator
{
    private readonly System.Threading.Lock managementGate = new();
    private readonly CancellationTokenSource managementLifetime = new();
    private readonly HashSet<Task> managementRuns = [];
    private bool managementStopped;

    public IObservable<LiveDaqManagementAvailability> ObserveManagementAvailability(string identityKey) =>
        liveDaqCatalogService.Observe()
            .Select(entries => entries.FirstOrDefault(entry => string.Equals(entry.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase)))
            .CombineLatest(streamRegistry.ObserveTransport(identityKey),
                (device, holder) => new LiveDaqManagementAvailability(device, holder))
            .DistinctUntilChanged();

    public Task<LiveDaqManagementResult> SetTimeAsync(string identityKey, CancellationToken cancellationToken = default) =>
        RunManagementAsync(identityKey, DaqManagementOperation.SetTime, DaqManagementFailureReason.SetTime, async (device, token) =>
            await managementService.SetTimeAsync(device.Host, device.Port, token) switch
            {
                DaqSetTimeResult.Ok ok => new LiveDaqManagementResult.TimeSet(ok.RoundTripTime),
                DaqSetTimeResult.Error error => new LiveDaqManagementResult.Rejected(error.ErrorCode, error.Message),
                _ => throw new InvalidOperationException("Unexpected SET_TIME result.")
            }, cancellationToken);

    public Task<LiveDaqManagementResult> ReadConfigAsync(string identityKey, CancellationToken cancellationToken = default) =>
        RunManagementAsync(identityKey, DaqManagementOperation.ReadConfig, DaqManagementFailureReason.ReadConfig, async (device, token) =>
            await managementService.ReadConfigAsync(device.Host, device.Port, token) switch
            {
                DaqConfigReadResult.Read read => new LiveDaqManagementResult.ConfigRead(read.Document),
                DaqConfigReadResult.Error error => new LiveDaqManagementResult.Rejected(error.ErrorCode, error.Message),
                _ => throw new InvalidOperationException("Unexpected CONFIG read result.")
            }, cancellationToken);

    public Task<LiveDaqManagementResult> WriteConfigAsync(string identityKey, ReadOnlyMemory<byte> config, CancellationToken cancellationToken = default) =>
        RunManagementAsync(identityKey, DaqManagementOperation.WriteConfig, DaqManagementFailureReason.WriteConfig, async (device, token) =>
            await managementService.WriteConfigAsync(device.Host, device.Port, config.ToArray(), token) switch
            {
                DaqManagementResult.Ok => new LiveDaqManagementResult.ConfigWritten(),
                DaqManagementResult.Error error => new LiveDaqManagementResult.Rejected(error.ErrorCode, error.Message),
                _ => throw new InvalidOperationException("Unexpected CONFIG write result.")
            }, cancellationToken);

    public Task<LiveDaqManagementResult> SelectConfigFileAsync(CancellationToken cancellationToken = default) =>
        RunOwnedManagementAsync(async token =>
        {
            try
            {
                var file = await filesService.OpenDeviceConfigFileAsync(token);
                if (file is null) return new LiveDaqManagementResult.Canceled();
                if (!string.Equals(file.FileName, "CONFIG", StringComparison.Ordinal))
                {
                    logger.Warning("Selected device configuration file is not named CONFIG");
                    return new LiveDaqManagementResult.Failed(DaqManagementFailureReason.InvalidConfigName);
                }
                return new LiveDaqManagementResult.ConfigSelected(file);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.Error(exception, "Failed to select device configuration file");
                return new LiveDaqManagementResult.Failed(DaqManagementFailureReason.SelectConfigFile, exception.Message);
            }
        }, cancellationToken);

    private Task<LiveDaqManagementResult> RunManagementAsync(string identityKey, DaqManagementOperation operation,
        DaqManagementFailureReason failureReason, Func<LiveDaqCatalogEntry, CancellationToken, Task<LiveDaqManagementResult>> action,
        CancellationToken cancellationToken) => RunOwnedManagementAsync(token => backgroundTaskRunner.RunAsync<LiveDaqManagementResult>(async () =>
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var device = liveDaqCatalogService.Get(identityKey);
            if (device is null) return new LiveDaqManagementResult.Unavailable(DaqManagementUnavailability.Offline);
            if (!device.SupportsManagement) return new LiveDaqManagementResult.Unavailable(DaqManagementUnavailability.Unsupported);
            var acquired = await streamRegistry.AcquireManagementAsync(identityKey, operation);
            if (acquired is DaqTransportAcquisition.Busy busy) return new LiveDaqManagementResult.Busy(busy.Holder);
            using var reservation = ((DaqTransportAcquisition.Acquired)acquired).Lease;
            return await action(device, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Error(exception, "DAQ management {Operation} failed for {IdentityKey}", operation, identityKey);
            return new LiveDaqManagementResult.Failed(failureReason, exception.Message);
        }
    }, token), cancellationToken);

    private async Task<LiveDaqManagementResult> RunOwnedManagementAsync(
        Func<CancellationToken, Task<LiveDaqManagementResult>> action, CancellationToken cancellationToken)
    {
        Task<LiveDaqManagementResult> run;
        CancellationTokenSource linked;
        lock (managementGate)
        {
            if (managementStopped) throw new OperationCanceledException();
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, managementLifetime.Token);
            run = action(linked.Token);
            managementRuns.Add(run);
        }
        try { return await run; }
        finally
        {
            linked.Dispose();
            lock (managementGate) managementRuns.Remove(run);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (managementGate)
        {
            if (managementStopped) return;
            managementStopped = true;
            pending = [.. managementRuns];
        }
        managementLifetime.Cancel();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        managementLifetime.Dispose();
        Deactivate();
    }
}
