#if SUFNI_PROFILING_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Sufni.Profiling;

namespace Sufni.App.Infrastructure;

internal sealed class ProfilingResourceSampler : IDisposable
{
    private readonly string scenario;
    private readonly string correlationId;
    private readonly Process process = Process.GetCurrentProcess();
    private readonly Timer timer;
    private readonly long startAllocatedBytes;
    private readonly long startCpuTicks;
    private readonly int startGen0;
    private readonly int startGen1;
    private readonly int startGen2;
    private readonly DatabaseFootprint startDatabaseFootprint;
    private long peakManagedBytes;
    private long peakWorkingSetBytes;
    private long peakPrivateBytes;
    private int disposed;

    private ProfilingResourceSampler(string scenario, string correlationId)
    {
        this.scenario = scenario;
        this.correlationId = correlationId;
        process.Refresh();
        startAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        startCpuTicks = process.TotalProcessorTime.Ticks;
        startGen0 = GC.CollectionCount(0);
        startGen1 = GC.CollectionCount(1);
        startGen2 = GC.CollectionCount(2);
        startDatabaseFootprint = GetDatabaseFootprint();
        Sample();
        timer = new Timer(_ => Sample(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    internal static ProfilingResourceSampler? Start(string scenario, string correlationId) =>
        ProfilingRuntime.IsEnabled ? new ProfilingResourceSampler(scenario, correlationId) : null;

    internal void Record(string markerName, string? context = null)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Sample();
        process.Refresh();
        var endDatabaseFootprint = GetDatabaseFootprint();
        var resourceValues = string.Join(';',
        [
            Pair("allocatedBytesDelta", GC.GetTotalAllocatedBytes(precise: false) - startAllocatedBytes),
            Pair("cpuMillisecondsDelta", (process.TotalProcessorTime.Ticks - startCpuTicks) / (double)TimeSpan.TicksPerMillisecond),
            Pair("managedBytesEnd", GC.GetTotalMemory(forceFullCollection: false)),
            Pair("managedBytesPeak", Interlocked.Read(ref peakManagedBytes)),
            Pair("workingSetBytesEnd", process.WorkingSet64),
            Pair("workingSetBytesPeak", Interlocked.Read(ref peakWorkingSetBytes)),
            Pair("privateBytesEnd", process.PrivateMemorySize64),
            Pair("privateBytesPeak", Interlocked.Read(ref peakPrivateBytes)),
            Pair("gen0Delta", GC.CollectionCount(0) - startGen0),
            Pair("gen1Delta", GC.CollectionCount(1) - startGen1),
            Pair("gen2Delta", GC.CollectionCount(2) - startGen2),
            Pair("databaseBytesStart", startDatabaseFootprint.TotalBytes),
            Pair("databaseBytesEnd", endDatabaseFootprint.TotalBytes),
            Pair("databaseBytesDelta", endDatabaseFootprint.TotalBytes - startDatabaseFootprint.TotalBytes),
            Pair("databaseMainBytesStart", startDatabaseFootprint.MainBytes),
            Pair("databaseMainBytesEnd", endDatabaseFootprint.MainBytes),
            Pair("databaseWalBytesStart", startDatabaseFootprint.WalBytes),
            Pair("databaseWalBytesEnd", endDatabaseFootprint.WalBytes),
            Pair("databaseShmBytesStart", startDatabaseFootprint.ShmBytes),
            Pair("databaseShmBytesEnd", endDatabaseFootprint.ShmBytes),
        ]);
        var value = string.IsNullOrWhiteSpace(context)
            ? resourceValues
            : $"{context};{resourceValues}";
        ProfilingRuntime.Marker(scenario, markerName, correlationId, value);
        ProfilingRuntime.Flush();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        timer.Dispose();
        process.Dispose();
    }

    private void Sample()
    {
        try
        {
            process.Refresh();
            SetMax(ref peakManagedBytes, GC.GetTotalMemory(forceFullCollection: false));
            SetMax(ref peakWorkingSetBytes, process.WorkingSet64);
            SetMax(ref peakPrivateBytes, process.PrivateMemorySize64);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void SetMax(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private static DatabaseFootprint GetDatabaseFootprint()
    {
        var databasePath = AppPaths.DatabasePath;
        return new DatabaseFootprint(
            FileLength(databasePath),
            FileLength(databasePath + "-wal"),
            FileLength(databasePath + "-shm"));
    }

    private static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static string Pair(string name, long value) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}={value}");

    private static string Pair(string name, double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}={value:F3}");

    private readonly record struct DatabaseFootprint(long MainBytes, long WalBytes, long ShmBytes)
    {
        internal long TotalBytes => MainBytes + WalBytes + ShmBytes;
    }
}
#endif
