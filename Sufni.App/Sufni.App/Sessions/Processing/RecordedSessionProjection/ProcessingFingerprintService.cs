using Sufni.App.Bikes.Services;
using System;
using System.Text.Json;
using Sufni.Telemetry;

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
    private const int SchemaVersion = 4;

    public int CurrentSchemaVersion => SchemaVersion;

    public ProcessingFingerprint CreateCurrent(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source) =>
        CreateCurrent(
            session,
            setup,
            bike,
            source,
            ProcessingDependencyHash.Compute(setup, bike));

    public ProcessingFingerprint CreateCurrent(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source,
        string dependencyHash) =>
        CreateCurrentDatabaseInputs(session, setup, bike, source, dependencyHash) with
        {
            VelocityFilterWindowMilliseconds =
                session.ProcessingOptions.ClampedVelocityFilterWindowMilliseconds,
        };

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

        var window = input.Session.DerivationWindow;
        if (input.Source.SessionId != (window?.SourceSessionId ?? input.Session.Id))
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
            input.Source.SourceHash,
            VelocityFilterWindowMilliseconds: new TelemetryProcessingOptions(input.Session.VelocityFilterWindowMilliseconds ??
                TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds).ClampedVelocityFilterWindowMilliseconds,
            DerivationWindow: window,
            CalibrationVersion: TelemetryCalibrationVersion.Current);
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

        if (source.SessionId != session.RecordingSourceSessionId)
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
            source.SourceHash,
            DerivationWindow: session.DerivationWindow,
            CalibrationVersion: TelemetryCalibrationVersion.Current);
    }

    private ProcessingFingerprint? ParsePersisted(SessionSnapshot session) =>
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

    public ProcessingFingerprintEvaluation EvaluateState(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        string? dependencyHash)
    {
        var options = session.ProcessingOptions;
        var persisted = ParsePersisted(session);
        var current = setup is not null && bike is not null && source is not null
            ? CreateCurrent(
                session,
                setup,
                bike,
                source,
                dependencyHash ?? ProcessingDependencyHash.Compute(setup, bike))
            : null;
        var staleness = Evaluate(session, setup, bike, source, persisted, current, options, dependencyHash);

        return new ProcessingFingerprintEvaluation(current, persisted, staleness);
    }

    private static SessionStaleness Evaluate(
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

        return session.HasProcessedData && current is not null && persisted == current
            ? new SessionStaleness.Current()
            : new SessionStaleness.Stale();
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
               persisted.CalibrationVersion != TelemetryCalibrationVersion.Current ||
               persisted.VelocityFilterWindowMilliseconds != options.ClampedVelocityFilterWindowMilliseconds ||
               persisted.DerivationWindow != session.DerivationWindow ||
               !StringComparer.Ordinal.Equals(persisted.DependencyHash, currentDependencyHash);
    }
}
