using System;
using System.Text.Json;
using Sufni.App.Models;
using Sufni.App.Stores;
using Sufni.Telemetry;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Creates, parses, and evaluates processing fingerprints for recorded
/// sessions. It classifies processed data as current, stale, missing, legacy,
/// or blocked by missing inputs.
/// </summary>
public sealed class ProcessingFingerprintService : IProcessingFingerprintService
{
    private const int SchemaVersion = 1;

    public ProcessingFingerprint CreateCurrent(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source)
    {
        if (session.SetupId != setup.Id)
        {
            throw new InvalidOperationException("Session setup does not match the processing setup.");
        }

        if (setup.BikeId != bike.Id)
        {
            throw new InvalidOperationException("Setup bike does not match the processing bike.");
        }

        if (source.SessionId != session.Id)
        {
            throw new InvalidOperationException("Recorded source does not match the processing session.");
        }

        return new ProcessingFingerprint(
            SchemaVersion,
            TelemetryProcessingVersion.Current,
            setup.Id,
            bike.Id,
            ProcessingDependencyHash.Compute(setup, bike),
            source.SourceHash);
    }

    public ProcessingFingerprint? ParsePersisted(SessionSnapshot session)
    {
        if (string.IsNullOrWhiteSpace(session.ProcessingFingerprintJson))
        {
            return null;
        }

        try
        {
            return AppJson.Deserialize<ProcessingFingerprint>(session.ProcessingFingerprintJson);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public SessionStaleness Evaluate(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source)
    {
        var persisted = ParsePersisted(session);
        return Evaluate(session, setup, bike, source, persisted, current: null);
    }

    public ProcessingFingerprintEvaluation EvaluateState(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source)
    {
        var persisted = ParsePersisted(session);
        var current = setup is not null && bike is not null && source is not null
            ? CreateCurrent(session, setup, bike, source)
            : null;
        var staleness = Evaluate(session, setup, bike, source, persisted, current);

        return new ProcessingFingerprintEvaluation(current, persisted, staleness);
    }

    private SessionStaleness Evaluate(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        ProcessingFingerprint? persisted,
        ProcessingFingerprint? current)
    {
        if (source is null)
        {
            return new SessionStaleness.MissingRawSource(
                IsProcessedStateStaleWithoutRawSource(session, setup, bike, persisted));
        }

        if (setup is null || bike is null)
        {
            return new SessionStaleness.MissingDependencies(setup is null, bike is null);
        }

        if (!session.HasProcessedData)
        {
            return new SessionStaleness.MissingProcessedData();
        }

        if (persisted is null || persisted.SchemaVersion != SchemaVersion)
        {
            return new SessionStaleness.UnknownLegacyFingerprint();
        }

        if (persisted.ProcessingVersion != TelemetryProcessingVersion.Current)
        {
            return new SessionStaleness.ProcessingVersionChanged(
                persisted.ProcessingVersion,
                TelemetryProcessingVersion.Current);
        }

        current ??= CreateCurrent(session, setup, bike, source);
        return persisted.SetupId != current.SetupId ||
               persisted.BikeId != current.BikeId ||
               !StringComparer.Ordinal.Equals(persisted.DependencyHash, current.DependencyHash) ||
               !StringComparer.Ordinal.Equals(persisted.SourceHash, current.SourceHash)
            ? new SessionStaleness.DependencyHashChanged()
            : new SessionStaleness.Current();
    }

    private static bool IsProcessedStateStaleWithoutRawSource(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        ProcessingFingerprint? persisted)
    {
        if (setup is null || bike is null)
        {
            return true;
        }

        if (!session.HasProcessedData)
        {
            return true;
        }

        if (persisted is null || persisted.SchemaVersion != SchemaVersion)
        {
            return true;
        }

        if (persisted.ProcessingVersion != TelemetryProcessingVersion.Current)
        {
            return true;
        }

        return persisted.SetupId != setup.Id ||
               persisted.BikeId != bike.Id ||
               !StringComparer.Ordinal.Equals(
                   persisted.DependencyHash,
                   ProcessingDependencyHash.Compute(setup, bike));
    }
}
