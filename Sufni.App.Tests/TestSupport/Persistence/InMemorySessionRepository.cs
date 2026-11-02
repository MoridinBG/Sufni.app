using NSubstitute;
using Sufni.App.Sessions.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemorySessionRepository
{
    private readonly Dictionary<Guid, SessionSnapshot> snapshots = [];
    private readonly Dictionary<Guid, byte[]> telemetry = [];
    private readonly Dictionary<Guid, RecordedSessionSourceSnapshot> sources = [];
    private readonly Dictionary<Guid, SessionBlobSwap> swaps = [];
    public ISessionRepository Repository { get; } = Substitute.For<ISessionRepository>();
    public ISessionBlobSwapRequestStore SwapRequests { get; } = Substitute.For<ISessionBlobSwapRequestStore>();
    public Exception? WriteFailure { get; set; }

    public InMemorySessionRepository(TimeProvider clock)
    {
        SwapRequests.GetRequestAsync(Arg.Any<Guid>()).Returns(call => swaps.GetValueOrDefault(call.Arg<Guid>()));
        SwapRequests.GetRequestedSessionIdsAsync().Returns(_ => swaps.Keys.ToList());
        SwapRequests.CompleteRequestAsync(Arg.Any<Guid>(), Arg.Any<string>()).Returns(call =>
        {
            var id = call.Arg<Guid>();
            if (swaps.GetValueOrDefault(id)?.TargetFingerprint == call.Arg<string>()) swaps.Remove(id);
            return Task.CompletedTask;
        });
        Repository.GetSessionAsync(Arg.Any<Guid>()).Returns(call =>
            ToEntity(snapshots.GetValueOrDefault(call.Arg<Guid>())));
        Repository.GetSessionsAsync().Returns(_ => snapshots.Values.Select(snapshot => ToEntity(snapshot)!).ToList());
        Repository.GetSessionRawPsstAsync(Arg.Any<Guid>()).Returns(call => telemetry.GetValueOrDefault(call.Arg<Guid>()));
        Repository.GetImportCheckpointAsync(Arg.Any<string>()).Returns(call =>
        {
            var existing = sources.Values.FirstOrDefault(source => source.SourceHash == call.Arg<string>());
            return existing is null ? null : new SessionImportCommitResult.Exists(
                ToEntity(snapshots.GetValueOrDefault(existing.SessionId)), existing);
        });
        Repository.ImportProcessedSessionAsync(Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<RecordedSessionSource>())
            .Returns(call =>
            {
                if (WriteFailure is { } failure) return Task.FromException<SessionImportCommitResult>(failure);
                var source = call.Arg<RecordedSessionSource>();
                var existing = sources.Values.FirstOrDefault(value => value.SourceHash == source.SourceHash);
                if (existing is not null)
                    return Task.FromResult<SessionImportCommitResult>(new SessionImportCommitResult.Exists(ToEntity(snapshots.GetValueOrDefault(existing.SessionId)), existing));
                var session = call.Arg<Session>();
                if (!snapshots.ContainsKey(session.Id)) PersistProcessed(session);
                var sourceSnapshot = RecordedSessionSourceSnapshot.From(source);
                sources[session.Id] = sourceSnapshot;
                return Task.FromResult<SessionImportCommitResult>(new SessionImportCommitResult.Inserted(ToEntity(snapshots[session.Id]), sourceSnapshot));
            });

        Session PersistProcessed(Session session)
        {
            var snapshot = SessionSnapshot.From(session) with
            {
                Updated = clock.GetUtcNow().ToUnixTimeSeconds(),
            };
            snapshots[session.Id] = snapshot;
            if (session.ProcessedData is { } bytes) telemetry[session.Id] = bytes;
            return ToEntity(snapshot)!;
        }
        Repository.UpdateSessionPsstAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string?>(),
                Arg.Any<double?>(), Arg.Any<SessionProcessedWriteGuard>())
            .Returns(call => CommitTransferred(call.Arg<Guid>(), call.Arg<byte[]>(), call.Arg<string?>(),
                call.Arg<double?>(), call.Arg<SessionProcessedWriteGuard>(), null));
        Repository.UpdateSessionProcessedGenerationAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string?>(),
                Arg.Any<SessionProcessedGeneration>(), Arg.Any<SessionProcessedWriteGuard>())
            .Returns(call =>
            {
                var generation = call.Arg<SessionProcessedGeneration>();
                return CommitTransferred(call.Arg<Guid>(), call.Arg<byte[]>(), call.Arg<string?>(),
                    generation.DurationSeconds,
                    call.Arg<SessionProcessedWriteGuard>(), generation);
            });

        Task<bool> CommitTransferred(Guid id, byte[] bytes, string? fingerprint, double? duration,
            SessionProcessedWriteGuard expected, SessionProcessedGeneration? generation)
        {
            if (WriteFailure is { } failure) return Task.FromException<bool>(failure);
            if (!snapshots.TryGetValue(id, out var current) || current.ProcessingFingerprintJson != expected.Fingerprint ||
                current.ProcessedTelemetryRevision != expected.Revision || swaps.GetValueOrDefault(id)?.TargetFingerprint != expected.PendingSwapFingerprint)
                return Task.FromResult(false);
            var changed = !telemetry.TryGetValue(id, out var held) || !held.SequenceEqual(bytes) || fingerprint != current.ProcessingFingerprintJson;
            var saved = current with
            {
                HasProcessedData = true, ProcessingFingerprintJson = fingerprint,
                ProcessedTelemetryRevision = current.ProcessedTelemetryRevision + (changed ? 1 : 0),
                DurationSeconds = duration
            };
            snapshots[id] = saved;
            telemetry[id] = bytes;
            swaps.Remove(id);
            return Task.FromResult(true);
        }

        Repository.UpdateGpsAlignmentAsync(Arg.Any<Guid>(), Arg.Any<double>())
            .Returns(call =>
            {
                if (WriteFailure is { } failure) return Task.FromException(failure);
                var id = call.Arg<Guid>();
                var current = snapshots[id];
                snapshots[id] = current with
                {
                    GpsOffsetSeconds = call.Arg<double>(),
                    Updated = clock.GetUtcNow().ToUnixTimeSeconds()
                };
                return Task.CompletedTask;
            });
        Repository.SetProcessingOptionAsync(Arg.Any<Guid>(), Arg.Any<int>()).Returns(call =>
            SetWindow(call.Arg<Guid>(), call.ArgAt<int>(1)));
        Repository.RestoreProcessingOptionAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
        {
            var id = call.Arg<Guid>();
            return snapshots.GetValueOrDefault(id)?.VelocityFilterWindowMilliseconds == call.ArgAt<int>(1)
                ? SetWindow(id, call.ArgAt<int?>(2))
                : Task.FromResult(ToEntity(snapshots.GetValueOrDefault(id)));
        });

        Task<Session?> SetWindow(Guid id, int? window)
        {
            if (WriteFailure is { } failure) return Task.FromException<Session?>(failure);
            if (!snapshots.TryGetValue(id, out var current)) return Task.FromResult<Session?>(null);
            var saved = current with { VelocityFilterWindowMilliseconds = window, Updated = clock.GetUtcNow().ToUnixTimeSeconds() };
            snapshots[id] = saved;
            return Task.FromResult<Session?>(ToEntity(saved));
        }

        Repository.PutSessionAsync(Arg.Any<Session>()).Returns(call =>
        {
            if (WriteFailure is { } failure) return Task.FromException<Guid>(failure);
            var draft = call.Arg<Session>();
            var saved = SessionSnapshot.From(draft) with { Updated = clock.GetUtcNow().ToUnixTimeSeconds() };
            if (snapshots.TryGetValue(draft.Id, out var current))
            {
                saved = saved with
                {
                    Timestamp = current.Timestamp,
                    GpsOffsetSeconds = current.GpsOffsetSeconds,
                    HasProcessedData = current.HasProcessedData,
                    ProcessingFingerprintJson = current.ProcessingFingerprintJson,
                    DurationSeconds = current.DurationSeconds,
                    DistanceMeters = current.DistanceMeters,
                    AscentMeters = current.AscentMeters,
                    DescentMeters = current.DescentMeters,
                    ProcessedTelemetryRevision = current.ProcessedTelemetryRevision,
                    VelocityFilterWindowMilliseconds = current.VelocityFilterWindowMilliseconds,
                };
            }
            snapshots[draft.Id] = saved;
            return Task.FromResult(draft.Id);
        });
    }

    private static Session? ToEntity(SessionSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        var entity = snapshot.ToMetadataEntity();
        // This fake models BLOB presence without retaining telemetry bytes.
        entity.ProcessedData = snapshot.HasProcessedData ? [] : null;
        entity.DistanceMeters = snapshot.DistanceMeters;
        entity.AscentMeters = snapshot.AscentMeters;
        entity.DescentMeters = snapshot.DescentMeters;
        entity.GpsMetricsKey = snapshot.GpsMetricsKey;
        return entity;
    }

    public void Seed(SessionSnapshot snapshot) => snapshots[snapshot.Id] = snapshot;

    public void Seed(Session session)
    {
        Seed(SessionSnapshot.From(session));
        if (session.ProcessedData is { } bytes) telemetry[session.Id] = bytes;
    }

    public void SeedSwap(SessionBlobSwap swap) => swaps[swap.SessionId] = swap;
}
