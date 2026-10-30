using NSubstitute;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemorySessionRepository
{
    private readonly Dictionary<Guid, SessionSnapshot> snapshots = [];
    public ISessionRepository Repository { get; } = Substitute.For<ISessionRepository>();
    public Exception? WriteFailure { get; set; }

    public InMemorySessionRepository(TimeProvider clock)
    {
        Repository.GetSessionAsync(Arg.Any<Guid>()).Returns(call =>
            snapshots.GetValueOrDefault(call.Arg<Guid>())?.ToMetadataEntity());
        Repository.GetSessionsAsync().Returns(_ => snapshots.Values.Select(snapshot => snapshot.ToMetadataEntity()).ToList());
        Repository.SetProcessingOptionAsync(Arg.Any<Guid>(), Arg.Any<int>()).Returns(call =>
            SetWindow(call.Arg<Guid>(), call.ArgAt<int>(1)));
        Repository.RestoreProcessingOptionAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
        {
            var id = call.Arg<Guid>();
            return snapshots.GetValueOrDefault(id)?.VelocityFilterWindowMilliseconds == call.ArgAt<int>(1)
                ? SetWindow(id, call.ArgAt<int?>(2))
                : Task.FromResult(snapshots.GetValueOrDefault(id)?.ToMetadataEntity());
        });

        Task<Session?> SetWindow(Guid id, int? window)
        {
            if (WriteFailure is { } failure) return Task.FromException<Session?>(failure);
            if (!snapshots.TryGetValue(id, out var current)) return Task.FromResult<Session?>(null);
            var saved = current with { VelocityFilterWindowMilliseconds = window, Updated = clock.GetUtcNow().ToUnixTimeSeconds() };
            snapshots[id] = saved;
            return Task.FromResult<Session?>(saved.ToMetadataEntity());
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
                    HasProcessedData = current.HasProcessedData,
                    ProcessingFingerprintJson = current.ProcessingFingerprintJson,
                    FullTrackId = current.FullTrackId,
                    DurationSeconds = current.DurationSeconds,
                    DistanceMeters = current.DistanceMeters,
                    AscentMeters = current.AscentMeters,
                    DescentMeters = current.DescentMeters,
                    ProcessedTelemetryRevision = current.ProcessedTelemetryRevision,
                    TrackProjectionRevision = current.TrackProjectionRevision,
                };
            }
            snapshots[draft.Id] = saved;
            return Task.FromResult(draft.Id);
        });
    }

    public void Seed(SessionSnapshot snapshot) => snapshots[snapshot.Id] = snapshot;
}
