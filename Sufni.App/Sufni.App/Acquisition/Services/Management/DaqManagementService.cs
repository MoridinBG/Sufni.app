using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;

namespace Sufni.App.Acquisition.Services.Management;

internal sealed class DaqManagementService : IDaqManagementService
{
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan connectTimeout;
    private readonly TimeSpan ioTimeout;
    private readonly TimeSpan commitTimeout;

    public DaqManagementService(TimeProvider timeProvider)
        : this(
            timeProvider,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15))
    {
    }

    internal DaqManagementService(
        TimeProvider timeProvider,
        TimeSpan connectTimeout,
        TimeSpan ioTimeout,
        TimeSpan commitTimeout)
    {
        this.timeProvider = timeProvider;
        this.connectTimeout = connectTimeout;
        this.ioTimeout = ioTimeout;
        this.commitTimeout = commitTimeout;
    }

    public async Task<DaqListDirectoryResult> ListDirectoryAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        await client.ConnectAsync(endpoint, cancellationToken);
        return await client.ListDirectoryAsync(DaqDirectoryId.Root, cancellationToken);
    }

    public async Task<DaqConfigReadResult> ReadConfigAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        await client.ConnectAsync(endpoint, cancellationToken);
        using var destination = new MemoryStream();
        var result = await client.GetFileAsync(DaqFileClass.Config, 0, destination, cancellationToken);
        return result switch
        {
            DaqGetFileResult.Downloaded => new DaqConfigReadResult.Read(DaqConfigDocument.Parse(destination.ToArray())),
            DaqGetFileResult.Error error => new DaqConfigReadResult.Error(error.ErrorCode, error.Message),
            _ => throw new DaqManagementException("CONFIG download returned an unsupported result shape.")
        };
    }

    public async Task<DaqManagementResult> MarkSstUploadedAsync(
        ServiceEndpoint endpoint,
        int recordId,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        await client.ConnectAsync(endpoint, cancellationToken);
        return await client.MarkSstUploadedAsync(recordId, cancellationToken);
    }

    public async Task<DaqManagementResult> TrashFileAsync(
        ServiceEndpoint endpoint,
        int recordId,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        await client.ConnectAsync(endpoint, cancellationToken);
        return await client.TrashFileAsync(recordId, cancellationToken);
    }

    public async Task<DaqSetTimeResult> SetTimeAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        await client.ConnectAsync(endpoint, cancellationToken);

        var samples = new TimeSyncSample[5];
        for (var i = 0; i < samples.Length; i++)
        {
            var utcBeforeSend = timeProvider.GetUtcNow();
            var stampBeforeSend = timeProvider.GetTimestamp();
            await client.PingAsync(cancellationToken);
            var roundTripTime = timeProvider.GetElapsedTime(stampBeforeSend, timeProvider.GetTimestamp());
            samples[i] = new TimeSyncSample(
                utcBeforeSend + TimeSpan.FromTicks(roundTripTime.Ticks / 2),
                roundTripTime);
        }

        var trimmedSamples = samples
            .OrderBy(sample => sample.RoundTripTime)
            .Skip(1)
            .Take(3)
            .ToArray();

        var averageMidpointTicks = trimmedSamples.Sum(sample => sample.Midpoint.UtcTicks) / trimmedSamples.Length;
        var averageRoundTripTicks = trimmedSamples.Sum(sample => sample.RoundTripTime.Ticks) / trimmedSamples.Length;
        var targetInstant = new DateTimeOffset(averageMidpointTicks, TimeSpan.Zero);
        var targetSeconds = targetInstant.ToUnixTimeSeconds();
        var microseconds = (uint)((targetInstant - DateTimeOffset.FromUnixTimeSeconds(targetSeconds)).Ticks / TimeSpan.TicksPerMicrosecond);

        var result = await client.SetTimeAsync((uint)targetSeconds, microseconds, cancellationToken);
        return result switch
        {
            DaqManagementResult.Ok => new DaqSetTimeResult.Ok(TimeSpan.FromTicks(averageRoundTripTicks)),
            DaqManagementResult.Error error => new DaqSetTimeResult.Error(error.ErrorCode, error.Message),
            _ => throw new DaqManagementException("SET_TIME returned an unsupported result shape.")
        };
    }

    public async Task<IDaqManagementSession> OpenSessionAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        try
        {
            await client.ConnectAsync(endpoint, cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new DaqManagementSession(client);
    }

    public async Task<DaqManagementResult> WriteConfigAsync(
        ServiceEndpoint endpoint,
        byte[] configBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configBytes);

        using var client = CreateClient();
        await client.ConnectAsync(endpoint, cancellationToken);
        return await client.ReplaceConfigAsync(configBytes, cancellationToken);
    }

    private ManagementClient CreateClient() =>
        new(connectTimeout, ioTimeout, commitTimeout);

    private readonly record struct TimeSyncSample(DateTimeOffset Midpoint, TimeSpan RoundTripTime);
}
