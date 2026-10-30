using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Video.Services;
namespace Sufni.App.Sessions.Coordination;

internal sealed class SessionPersistenceTransactionRunner(
    SqliteConnectionContext connectionContext,
    TimeProvider timeProvider)
    : ISessionPersistenceTransactionRunner
{
    private const string EditableSessionProjection = """
                                                     SELECT
                                                         id, name, description, setup_id, timestamp, gps_offset_seconds, velocity_filter_window_ms,
                                                         source_session_id, window_start_seconds, window_end_seconds,
                                                         previous_window_start_seconds, previous_window_end_seconds,
                                                         front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                                         rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr
                                                     FROM session
                                                     WHERE id = ? AND deleted IS NULL
                                                     """;

    private const string UpdateEditedSessionSql = """
                                                  UPDATE session
                                                  SET
                                                      name=?,
                                                      timestamp=?,
                                                      gps_offset_seconds=?,
                                                      source_session_id=?,
                                                      window_start_seconds=?,
                                                      window_end_seconds=?,
                                                      previous_window_start_seconds=?,
                                                      previous_window_end_seconds=?,
                                                      updated=?
                                                  WHERE id=?
                                                  """;

    public async Task<bool> DeleteSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var sourceDeleted = false;
        await connectionContext.RunInTransactionAsync(connection =>
        {
            var fullTrackId = connection.Query<Session>(
                "SELECT full_track_id FROM session WHERE id = ? AND deleted IS NULL", sessionId)
                .SingleOrDefault()?.FullTrack;
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Session>.DeleteInTransaction(connection, sessionId, now);

            if (!RecordedSessionSourceRepository.IsSourceReferencedInTransaction(connection, sessionId))
            {
                RecordedSessionSourceRepository.DeleteRecordedSessionSourceInTransaction(connection, sessionId);
                sourceDeleted = true;
            }

            if (fullTrackId.HasValue && connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM session WHERE full_track_id = ? AND deleted IS NULL", fullTrackId.Value) == 0)
            {
                SynchronizableRepository<Track>.DeleteInTransaction(connection, fullTrackId.Value, now);
            }
        }, cancellationToken);

        return sourceDeleted;
    }

    public Task<SessionEditCommit> TrimUntilEndAsync(SessionSnapshot session, double clickSeconds) =>
        EditAsync(session, SessionWindowEditKind.TrimUntilEnd, clickSeconds, (connection, row, plan, now) =>
        {
            Update(connection, row.Id, plan.Current, now);
            return new SessionEditCommit.Committed([row.Id], [], []);
        });

    public Task<SessionEditCommit> TrimFromBeginningAsync(SessionSnapshot session, double clickSeconds) =>
        EditAsync(session, SessionWindowEditKind.TrimFromBeginning, clickSeconds, ApplyOriginEdit);

    public Task<SessionEditCommit> UndoTrimAsync(SessionSnapshot session) =>
        EditAsync(session, SessionWindowEditKind.UndoTrim, 0, ApplyOriginEdit);

    private static SessionEditCommit ApplyOriginEdit(SQLiteConnection connection, Session row,
        SessionWindowEditPlan.Planned plan, long now)
    {
        Update(connection, row.Id, plan.Current, now);
        var clips = SessionVideoRepository.ShiftAlignmentInTransaction(connection, row.Id, plan.ClipAlignmentDelta);
        var segments = SessionSegmentRepository.ShiftInTransaction(connection, row.Id, plan.SegmentShiftSeconds, now);
        return new SessionEditCommit.Committed([row.Id], clips, segments);
    }

    public Task<SessionEditCommit> SplitAsync(SessionSnapshot session, double clickSeconds) =>
        EditAsync(session, SessionWindowEditKind.Split, clickSeconds, (connection, row, plan, now) =>
        {
            var second = plan.Created!;
            var part2 = new Session(Guid.NewGuid(), second.Name, row.Description, row.Setup, second.Timestamp)
            {
                VelocityFilterWindowMilliseconds = row.VelocityFilterWindowMilliseconds,
                GpsOffsetSeconds = second.GpsOffsetSeconds,
                SourceSessionId = second.Window!.SourceSessionId,
                WindowStartSeconds = second.Window.StartSeconds,
                WindowEndSeconds = second.Window.EndSeconds,
                FrontSpringRate = row.FrontSpringRate,
                FrontHighSpeedCompression = row.FrontHighSpeedCompression,
                FrontLowSpeedCompression = row.FrontLowSpeedCompression,
                FrontLowSpeedRebound = row.FrontLowSpeedRebound,
                FrontHighSpeedRebound = row.FrontHighSpeedRebound,
                RearSpringRate = row.RearSpringRate,
                RearHighSpeedCompression = row.RearHighSpeedCompression,
                RearLowSpeedCompression = row.RearLowSpeedCompression,
                RearLowSpeedRebound = row.RearLowSpeedRebound,
                RearHighSpeedRebound = row.RearHighSpeedRebound,
                Updated = now,
            };
            PersistenceGuards.InsertEntity(connection, part2);

            Update(connection, row.Id, plan.Current, now);
            var clips = SessionVideoRepository.CopyToSessionInTransaction(
                connection,
                row.Id,
                part2.Id,
                plan.ClipAlignmentDelta,
                timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            var segments = SessionSegmentRepository.MoveToSessionInTransaction(
                connection,
                row.Id,
                part2.Id,
                plan.SplitSeconds!.Value,
                now);
            return new SessionEditCommit.Committed([row.Id, part2.Id], clips, segments, part2.Id);
        });

    private Task<SessionEditCommit> EditAsync(
        SessionSnapshot expected,
        SessionWindowEditKind kind,
        double clickSeconds,
        Func<SQLiteConnection, Session, SessionWindowEditPlan.Planned, long, SessionEditCommit> edit) =>
        connectionContext.RunInTransactionAsync(connection =>
        {
            var rows = connection.Query<Session>(EditableSessionProjection, expected.Id);
            if (rows.Count != 1)
            {
                return new SessionEditCommit.Rejected("Session is missing.");
            }

            var row = rows[0];
            if (row.SourceSessionId != expected.SourceSessionId ||
                row.WindowStartSeconds != expected.WindowStartSeconds ||
                row.WindowEndSeconds != expected.WindowEndSeconds ||
                row.PreviousWindowStartSeconds != expected.PreviousWindowStartSeconds ||
                row.PreviousWindowEndSeconds != expected.PreviousWindowEndSeconds)
            {
                return new SessionEditCommit.Rejected("Session changed while editing.");
            }

            return SessionWindowEditPolicy.Plan(SessionSnapshot.From(row), kind, clickSeconds) switch
            {
                SessionWindowEditPlan.Rejected rejected => new SessionEditCommit.Rejected(rejected.Message),
                SessionWindowEditPlan.Planned plan => edit(connection, row, plan, timeProvider.GetUtcNow().ToUnixTimeSeconds()),
                _ => throw new InvalidOperationException("Unknown session window plan.")
            };
        });

    private static void Update(SQLiteConnection connection, Guid sessionId, SessionWindowPatch patch, long now) =>
        connection.Execute(
            UpdateEditedSessionSql,
            patch.Name,
            patch.Timestamp,
            patch.GpsOffsetSeconds,
            patch.Window?.SourceSessionId,
            patch.Window?.StartSeconds ?? 0,
            patch.Window?.EndSeconds,
            patch.PreviousWindow?.StartSeconds,
            patch.PreviousWindow?.EndSeconds,
            now,
            sessionId);
}
