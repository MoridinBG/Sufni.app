#if SUFNI_PROFILING_DIAGNOSTICS
using System;
using System.Linq;
using Sufni.App.Sessions.Models;
using Sufni.Profiling;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services;

internal static class ProfilingBench01
{
    internal const string Scenario = "BENCH-01";

    private static readonly object Sync = new();
    private static string? activeRunId;
    private static string? currentCaptureCorrelationId;
    private static string? targetCaptureCorrelationId;
    private static Guid? savedSessionId;
    private static bool saveAttempted;
    private static bool finalized;

    internal static bool IsActive =>
        ProfilingRuntime.IsEnabled &&
        string.Equals(
            ProfilingRuntime.Options.Corpus,
            ProfilingLiveDaqReplay.Corpus,
            StringComparison.Ordinal);

    internal static bool ShouldStartCapture
    {
        get
        {
            lock (Sync)
            {
                EnsureRunStateLocked();
                return IsActive && !saveAttempted && !finalized;
            }
        }
    }

    internal static void CaptureStarted(string correlationId)
    {
        if (!IsActive)
        {
            return;
        }

        lock (Sync)
        {
            EnsureRunStateLocked();
            currentCaptureCorrelationId = correlationId;
        }
    }

    internal static void CaptureReachedTarget(string correlationId)
    {
        if (!IsActive)
        {
            return;
        }

        lock (Sync)
        {
            EnsureRunStateLocked();
            targetCaptureCorrelationId ??= correlationId;
        }
    }

    internal static void CaptureCompleted(string correlationId)
    {
        lock (Sync)
        {
            EnsureRunStateLocked();
            if (string.Equals(currentCaptureCorrelationId, correlationId, StringComparison.Ordinal))
            {
                currentCaptureCorrelationId = null;
            }
        }
    }

    internal static string? BeginSave()
    {
        if (!IsActive)
        {
            return null;
        }

        string? correlationId;
        lock (Sync)
        {
            EnsureRunStateLocked();
            saveAttempted = true;
            targetCaptureCorrelationId ??= currentCaptureCorrelationId;
            correlationId = targetCaptureCorrelationId;
        }

        ProfilingRuntime.Marker(
            Scenario,
            "LiveSave.Requested",
            correlationId,
            "status=requested");
        return correlationId;
    }

    internal static void SessionCreated(Guid sessionId, string? correlationId)
    {
        if (!IsActive)
        {
            return;
        }

        ProfilingRuntime.Marker(
            Scenario,
            "LiveSave.SessionCreated",
            correlationId,
            $"sessionId={sessionId:N}");
    }

    internal static void Saved(
        Guid sessionId,
        RecordedSessionSource source,
        LiveTelemetryCapture capture)
    {
        if (!IsActive)
        {
            return;
        }

        string? correlationId;
        lock (Sync)
        {
            EnsureRunStateLocked();
            savedSessionId = sessionId;
            correlationId = targetCaptureCorrelationId;
        }

        var frontCount = capture.FrontSegments.Sum(segment => (long)segment.Counts.LongLength);
        var rearCount = capture.RearSegments.Sum(segment => (long)segment.Counts.LongLength);
        var imuCounts = capture.ImuData is null
            ? string.Empty
            : string.Join(
                ',',
                capture.ImuData.Segments
                    .GroupBy(segment => segment.LocationId)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}:{group.Sum(segment => (long)segment.Records.LongLength)}"));
        ProfilingRuntime.Marker(
            Scenario,
            "LiveSave.Saved",
            correlationId,
            FormattableString.Invariant(
                $"sessionId={sessionId:N};sourceKind={source.SourceKind.StorageValue};sourceName={source.SourceName};sourceHash={source.SourceHash};payloadBytes={source.Payload.LongLength};sourceStartSeconds=0;sourceEndSeconds={capture.Metadata.Duration:F6};sourceBoundary=half-open;timestamp={capture.Metadata.Timestamp};front={frontCount};rear={rearCount};imu={imuCounts};gps={capture.GpsData?.LongLength ?? 0};temperature={capture.TemperatureData.LongLength};markers={capture.Markers.LongLength};gaps={capture.StreamGaps.LongLength};finalStatusPresent={capture.FinalStatus is not null};missingFinalStatus={capture.MissingFinalStatus}"));
        ProfilingRuntime.Flush();
    }

    internal static void SaveFailed(string? correlationId, string status, string? errorMessage = null)
    {
        if (!IsActive)
        {
            return;
        }

        ProfilingRuntime.Marker(
            Scenario,
            "LiveSave.Failed",
            correlationId,
            $"status={status};error={errorMessage ?? string.Empty}");
        ProfilingRuntime.Flush();
    }

    internal static void FinalizeAfterStop(string status)
    {
        if (!IsActive)
        {
            return;
        }

        string? correlationId;
        Guid? sessionId;
        lock (Sync)
        {
            EnsureRunStateLocked();
            if (!saveAttempted || finalized)
            {
                return;
            }

            finalized = true;
            correlationId = targetCaptureCorrelationId;
            sessionId = savedSessionId;
        }

        ProfilingRuntime.Marker(
            Scenario,
            "LiveStream.StopCompleted",
            correlationId,
            $"status={status};sessionId={sessionId?.ToString("N") ?? "none"}");
        var writerStatus = ProfilingRuntime.WriterStatus;
        ProfilingRuntime.Marker(
            Scenario,
            "Instrumentation.Status",
            correlationId,
            $"recordsWrittenBeforeFinalization={writerStatus.RecordsWritten};recordsDropped={writerStatus.RecordsDropped}");
        ProfilingRuntime.Marker(
            Scenario,
            "Timing.Finalizing",
            correlationId,
            $"status={status};sessionId={sessionId?.ToString("N") ?? "none"}");
        ProfilingRuntime.Flush();
        ProfilingRuntime.Shutdown();
    }

    private static void EnsureRunStateLocked()
    {
        var runId = ProfilingRuntime.Options.RunId;
        if (string.Equals(activeRunId, runId, StringComparison.Ordinal))
        {
            return;
        }

        activeRunId = runId;
        currentCaptureCorrelationId = null;
        targetCaptureCorrelationId = null;
        savedSessionId = null;
        saveAttempted = false;
        finalized = false;
    }
}
#endif
