using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services;

namespace Sufni.App.Services.Management;

internal sealed class DaqManagementService : IDaqManagementService
{
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan connectTimeout;
    private readonly TimeSpan ioTimeout;
    private readonly TimeSpan commitTimeout;

    public DaqManagementService()
        : this(TimeProvider.System)
    {
    }

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
        string host,
        int port,
        DaqDirectoryId directoryId,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(host, port);
        using var client = CreateClient();
        await client.ConnectAsync(host, port, cancellationToken);
        return await client.ListDirectoryAsync(directoryId, cancellationToken);
    }

    public async Task<DaqGetFileResult> GetFileAsync(
        string host,
        int port,
        DaqFileClass fileClass,
        int recordId,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(host, port);
        using var client = CreateClient();
        await client.ConnectAsync(host, port, cancellationToken);
        return await client.GetFileAsync(fileClass, recordId, cancellationToken);
    }

    public async Task<DaqManagementResult> TrashFileAsync(
        string host,
        int port,
        int recordId,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(host, port);
        using var client = CreateClient();
        await client.ConnectAsync(host, port, cancellationToken);
        return await client.TrashFileAsync(recordId, cancellationToken);
    }

    public async Task<DaqSetTimeResult> SetTimeAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(host, port);
        using var client = CreateClient();
        await client.ConnectAsync(host, port, cancellationToken);

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

    public async Task<DaqManagementResult> ReplaceConfigAsync(
        string host,
        int port,
        byte[] configBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configBytes);
        ValidateEndpoint(host, port);

        using var client = CreateClient();
        await client.ConnectAsync(host, port, cancellationToken);
        return await client.ReplaceConfigAsync(configBytes, cancellationToken);
    }

    private ManagementClient CreateClient() =>
        new(connectTimeout, ioTimeout, commitTimeout);

    private static void ValidateEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("A DAQ management host is required.", nameof(host));
        }

        if (port <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "A positive DAQ management port is required.");
        }
    }

    private readonly record struct TimeSyncSample(DateTimeOffset Midpoint, TimeSpan RoundTripTime);
}