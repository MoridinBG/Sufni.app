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
        TelemetryProcessingOptions? options = null) =>
        CreateCurrent(
            session,
            setup,
            bike,
            source,
            ProcessingDependencyHash.Compute(setup, bike),
            options);

    public ProcessingFingerprint CreateCurrent(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source,
        string dependencyHash,
        TelemetryProcessingOptions? options = null) =>
        CreateCurrentDatabaseInputs(session, setup, bike, source, dependencyHash) with
        {
            VelocityFilterWindowMilliseconds =
                (options ?? TelemetryProcessingOptions.Default).ClampedVelocityFilterWindowMilliseconds,
        };

    public ProcessingFingerprint CreateCurrentDatabaseInputs(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source)
    {
        return CreateCurrentDatabaseInputs(
            session,
            setup,
            bike,
            source,
            ProcessingDependencyHash.Compute(setup, bike));
    }

    public ProcessingFingerprint CreateCurrentDatabaseInputs(SessionProcessingInputBundle input)
    {
        if (input.Session.SetupId != input.Setup.Id)
        {
            throw new InvalidOperationException("Session setup does not match the processing setup.");
        }

        if (input.Setup.BikeId != input.Bike.Id)
        {
            throw new InvalidOperationException("Setup bike does not match the processing bike.");
        }

        if (input.Source.SessionId != input.Session.Id)
        {
            throw new InvalidOperationException("Recorded source does not match the processing session.");
        }

        return new ProcessingFingerprint(
            SchemaVersion,
            TelemetryProcessingVersion.Current,
            input.Setup.Id,
            input.Bike.Id,
            GpsTrackPointProjection.ProjectionVersion,
            ProcessingDependencyHash.Compute(input.Setup, input.Bike),
            input.Source.SourceHash);
    }

    private static ProcessingFingerprint CreateCurrentDatabaseInputs(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source,
        string dependencyHash)
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
            GpsTrackPointProjection.ProjectionVersion,
            dependencyHash,
            source.SourceHash);
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
        TelemetryProcessingOptions? options = null)
    {
        options ??= TelemetryProcessingOptions.Default;
        var persisted = ParsePersisted(session);
        return Evaluate(session, setup, bike, source, persisted, current: null, options, dependencyHash: null);
    }

    public ProcessingFingerprintEvaluation EvaluateState(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        TelemetryProcessingOptions? options = null)
    {
        var dependencyHash = setup is not null && bike is not null
            ? ProcessingDependencyHash.Compute(setup, bike)
            : null;
        return EvaluateState(session, setup, bike, source, dependencyHash, options);
    }

    public ProcessingFingerprintEvaluation EvaluateState(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        string? dependencyHash,
        TelemetryProcessingOptions? options = null)
    {
        options ??= TelemetryProcessingOptions.Default;
        var persisted = ParsePersisted(session);
        var current = setup is not null && bike is not null && source is not null
            ? CreateCurrent(
                session,
                setup,
                bike,
                source,
                dependencyHash ?? ProcessingDependencyHash.Compute(setup, bike),
                options)
            : null;
        var staleness = Evaluate(session, setup, bike, source, persisted, current, options, dependencyHash);

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
        string? dependencyHash)
    {
        if (source is null)
        {
            return new SessionStaleness.MissingRawSource(
                IsProcessedStateStaleWithoutRawSource(session, setup, bike, persisted, options, dependencyHash));
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

        current ??= CreateCurrent(session, setup, bike, source, options);
        return persisted.SetupId != current.SetupId ||
               persisted.BikeId != current.BikeId ||
               persisted.TrackProjectionVersion != current.TrackProjectionVersion ||
               persisted.VelocityFilterWindowMilliseconds != current.VelocityFilterWindowMilliseconds ||
               !StringComparer.Ordinal.Equals(persisted.DependencyHash, current.DependencyHash) ||
               !StringComparer.Ordinal.Equals(persisted.SourceHash, current.SourceHash)
            ? new SessionStaleness.DependencyHashChanged()
            : new SessionStaleness.Current();
    }

    private static bool IsProcessedStateStaleWithoutRawSource(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        ProcessingFingerprint? persisted,
        TelemetryProcessingOptions options,
        string? dependencyHash)
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
        var currentDependencyHash = dependencyHash ?? ProcessingDependencyHash.Compute(setup, bike);
        return persisted.SetupId != setup.Id ||
               persisted.BikeId != bike.Id ||
               persisted.TrackProjectionVersion != GpsTrackPointProjection.ProjectionVersion ||
               persisted.VelocityFilterWindowMilliseconds != options.ClampedVelocityFilterWindowMilliseconds ||
               !StringComparer.Ordinal.Equals(persisted.DependencyHash, currentDependencyHash);
    }
}
