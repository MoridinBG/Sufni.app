using System;
using System.Text.Json;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Creates, parses, and evaluates processing fingerprints for recorded
/// sessions. It classifies processed data as current, stale, missing, legacy,
/// or blocked by missing inputs.
/// </summary>
public sealed class ProcessingFingerprintService : IProcessingFingerprintService
{
    private const int SchemaVersion = 3;

    public int CurrentSchemaVersion => SchemaVersion;

    public ProcessingFingerprint CreateCurrent(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source,
        TelemetryProcessingOptions? options = null,
        RecordedSessionDerivationWindow? window = null) =>
        CreateCurrentDatabaseInputs(session, setup, bike, source, window) with
        {
            VelocityFilterWindowMilliseconds =
                (options ?? TelemetryProcessingOptions.Default).ClampedVelocityFilterWindowMilliseconds,
        };

    public ProcessingFingerprint CreateCurrentDatabaseInputs(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source,
        RecordedSessionDerivationWindow? window = null)
    {
        if (session.SetupId != setup.Id)
        {
            throw new InvalidOperationException("Session setup does not match the processing setup.");
        }

        if (setup.BikeId != bike.Id)
        {
            throw new InvalidOperationException("Setup bike does not match the processing bike.");
        }

        var expectedSourceSessionId = window?.SourceSessionId ?? session.Id;
        if (source.SessionId != expectedSourceSessionId)
        {
            throw new InvalidOperationException("Recorded source does not match the processing session.");
        }

        return new ProcessingFingerprint(
            SchemaVersion,
            TelemetryProcessingVersion.Current,
            setup.Id,
            bike.Id,
            GpsTrackPointProjection.ProjectionVersion,
            ProcessingDependencyHash.Compute(setup, bike),
            source.SourceHash,
            DerivationWindow: window);
    }

    public ProcessingFingerprint? ParsePersisted(SessionSnapshot session) =>
        Parse(session.ProcessingFingerprintJson);

    public ProcessingFingerprint? Parse(string? fingerprintJson)
    {
        if (string.IsNullOrWhiteSpace(fingerprintJson))
        {
            return null;
        }

        try
        {
            return AppJson.Deserialize<ProcessingFingerprint>(fingerprintJson);
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
        RecordedSessionSourceSnapshot? source,
        TelemetryProcessingOptions? options = null,
        RecordedSessionDerivationWindow? window = null)
    {
        options ??= TelemetryProcessingOptions.Default;
        var persisted = ParsePersisted(session);
        return Evaluate(session, setup, bike, source, persisted, current: null, options, window);
    }

    public ProcessingFingerprintEvaluation EvaluateState(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        TelemetryProcessingOptions? options = null,
        RecordedSessionDerivationWindow? window = null)
    {
        options ??= TelemetryProcessingOptions.Default;
        var persisted = ParsePersisted(session);
        var current = setup is not null && bike is not null && source is not null
            ? CreateCurrent(session, setup, bike, source, options, window)
            : null;
        var staleness = Evaluate(session, setup, bike, source, persisted, current, options, window);

        return new ProcessingFingerprintEvaluation(current, persisted, staleness);
    }

    private SessionStaleness Evaluate(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        ProcessingFingerprint? persisted,
        ProcessingFingerprint? current,
        TelemetryProcessingOptions options,
        RecordedSessionDerivationWindow? window)
    {
        if (source is null)
        {
            return new SessionStaleness.MissingRawSource(
                IsProcessedStateStaleWithoutRawSource(session, setup, bike, persisted, options, window));
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

        current ??= CreateCurrent(session, setup, bike, source, options, window);
        if (persisted.SetupId != current.SetupId ||
            persisted.BikeId != current.BikeId ||
            persisted.TrackProjectionVersion != current.TrackProjectionVersion ||
            persisted.VelocityFilterWindowMilliseconds != current.VelocityFilterWindowMilliseconds ||
            !StringComparer.Ordinal.Equals(persisted.DependencyHash, current.DependencyHash) ||
            !StringComparer.Ordinal.Equals(persisted.SourceHash, current.SourceHash))
        {
            return new SessionStaleness.DependencyHashChanged();
        }

        return persisted.DerivationWindow != current.DerivationWindow
            ? new SessionStaleness.SourceWindowChanged()
            : new SessionStaleness.Current();
    }

    private static bool IsProcessedStateStaleWithoutRawSource(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        ProcessingFingerprint? persisted,
        TelemetryProcessingOptions options,
        RecordedSessionDerivationWindow? window)
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

        // Mirror the source-backed comparison, including the processing option: a
        // source-less row whose processed data was computed with a different
        // velocity-filter window is stale (it just cannot self-heal by recompute).
        return persisted.SetupId != setup.Id ||
               persisted.BikeId != bike.Id ||
               persisted.TrackProjectionVersion != GpsTrackPointProjection.ProjectionVersion ||
               persisted.VelocityFilterWindowMilliseconds != options.ClampedVelocityFilterWindowMilliseconds ||
               persisted.DerivationWindow != window ||
               !StringComparer.Ordinal.Equals(
                   persisted.DependencyHash,
                   ProcessingDependencyHash.Compute(setup, bike));
    }
}
