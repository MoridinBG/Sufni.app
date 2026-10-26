using System;
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
    SqliteConnectionContext connectionContext)
    : ISessionPersistenceTransactionRunner
{
    private const string EditableSessionProjection = """
                                                     SELECT
                                                         id, name, description, setup_id, timestamp, gps_offset_seconds,
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
        Guid? fullTrackId,
        bool deleteFullTrack,
        CancellationToken cancellationToken = default)
    {
        var sourceDeleted = false;
        await connectionContext.RunInTransactionAsync(connection =>
        {
            SynchronizableRepository<Session>.DeleteInTransaction(connection, sessionId);

            if (!RecordedSessionSourceRepository.IsSourceReferencedInTransaction(connection, sessionId))
            {
                RecordedSessionSourceRepository.DeleteRecordedSessionSourceInTransaction(connection, sessionId);
                sourceDeleted = true;
            }

            if (deleteFullTrack && fullTrackId.HasValue)
            {
                SynchronizableRepository<Track>.DeleteInTransaction(connection, fullTrackId.Value);
            }
        }, cancellationToken);

        return sourceDeleted;
    }

    public Task<SessionEditCommit> TrimUntilEndAsync(SessionSnapshot session, double clickSeconds) =>
        EditAsync(session, (connection, row, now) =>
        {
            var window = EditWindow.Of(row);
            var split = window.Start + clickSeconds;
            if (!window.Contains(split))
            {
                return InvalidPosition;
            }

            Update(connection, row, window with { End = split }, previous: window, row.Name, now);
            return new SessionEditCommit.Committed([row.Id], [], []);
        });

    public Task<SessionEditCommit> TrimFromBeginningAsync(SessionSnapshot session, double clickSeconds) =>
        EditAsync(session, (connection, row, now) =>
        {
            var window = EditWindow.Of(row);
            var split = window.Start + clickSeconds;
            if (!window.Contains(split))
            {
                return InvalidPosition;
            }

            MoveOrigin(row, window.Start, split);
            Update(connection, row, window with { Start = split }, previous: window, row.Name, now);
            var clips = SessionVideoRepository.ShiftAlignmentInTransaction(
                connection,
                row.Id,
                FractionDelta(window.Start, split));
            var segments = SessionSegmentRepository.ShiftInTransaction(connection, row.Id, window.Start - split, now);
            return new SessionEditCommit.Committed([row.Id], clips, segments);
        });

    public Task<SessionEditCommit> UndoTrimAsync(SessionSnapshot session) =>
        EditAsync(session, (connection, row, now) =>
        {
            if (row.PreviousWindowStartSeconds is not { } previousStart)
            {
                return new SessionEditCommit.Rejected("Session has no trim to undo.");
            }

            var window = EditWindow.Of(row);
            var restored = window with { Start = previousStart, End = row.PreviousWindowEndSeconds };
            MoveOrigin(row, window.Start, restored.Start);
            Update(connection, row, restored, previous: null, row.Name, now);
            var clips = SessionVideoRepository.ShiftAlignmentInTransaction(
                connection,
                row.Id,
                FractionDelta(window.Start, restored.Start));
            var segments = SessionSegmentRepository.ShiftInTransaction(
                connection,
                row.Id,
                window.Start - restored.Start,
                now);
            return new SessionEditCommit.Committed([row.Id], clips, segments);
        });

    public Task<SessionEditCommit> SplitAsync(SessionSnapshot session, double clickSeconds) =>
        EditAsync(session, (connection, row, now) =>
        {
            var window = EditWindow.Of(row);
            var split = window.Start + clickSeconds;
            if (!window.Contains(split))
            {
                return InvalidPosition;
            }

            var part2 = new Session(Guid.NewGuid(), $"{row.Name} (2)", row.Description, row.Setup, row.Timestamp)
            {
                GpsOffsetSeconds = row.GpsOffsetSeconds,
                SourceSessionId = window.Source,
                WindowStartSeconds = split,
                WindowEndSeconds = window.End,
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
            MoveOrigin(part2, window.Start, split);
            PersistenceGuards.InsertEntity(connection, part2);

            Update(connection, row, window with { End = split }, previous: null, $"{row.Name} (1)", now);
            var clips = SessionVideoRepository.CopyToSessionInTransaction(
                connection,
                row.Id,
                part2.Id,
                FractionDelta(window.Start, split),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var segments = SessionSegmentRepository.MoveToSessionInTransaction(
                connection,
                row.Id,
                part2.Id,
                clickSeconds,
                now);
            return new SessionEditCommit.Committed([row.Id, part2.Id], clips, segments, part2.Id);
        });

    private static readonly SessionEditCommit InvalidPosition =
        new SessionEditCommit.Rejected("The position is outside the session.");

    private Task<SessionEditCommit> EditAsync(
        SessionSnapshot expected,
        Func<SQLiteConnection, Session, long, SessionEditCommit> edit) =>
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

            return edit(connection, row, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        });

    private static void Update(
        SQLiteConnection connection,
        Session row,
        EditWindow window,
        EditWindow? previous,
        string name,
        long now)
    {
        // The full own range is "no window".
        var hasWindow = window.Source != row.Id || window.Start != 0 || window.End is not null;
        connection.Execute(
            UpdateEditedSessionSql,
            name,
            row.Timestamp,
            row.GpsOffsetSeconds,
            hasWindow ? window.Source : null,
            hasWindow ? window.Start : 0,
            hasWindow ? window.End : null,
            previous?.Start,
            previous?.End,
            now,
            row.Id);
    }

    // The timestamp carries the whole seconds of the window start and the GPS
    // offset the fraction, so the telemetry and GPS timelines stay aligned.
    private static void MoveOrigin(Session session, double fromStartSeconds, double toStartSeconds)
    {
        if (session.Timestamp is not { } timestamp)
        {
            session.GpsOffsetSeconds = 0;
            return;
        }

        session.Timestamp = timestamp - (long)Math.Floor(fromStartSeconds) + (long)Math.Floor(toStartSeconds);
        session.GpsOffsetSeconds += FractionDelta(fromStartSeconds, toStartSeconds);
    }

    private static double FractionDelta(double fromSeconds, double toSeconds) =>
        toSeconds - Math.Floor(toSeconds) - (fromSeconds - Math.Floor(fromSeconds));

    private sealed record EditWindow(Guid Source, double Start, double? End)
    {
        public static EditWindow Of(Session session) => session.SourceSessionId is { } source
            ? new EditWindow(source, session.WindowStartSeconds, session.WindowEndSeconds)
            : new EditWindow(session.Id, 0, null);

        public bool Contains(double seconds) =>
            double.IsFinite(seconds) && Start < seconds && seconds < (End ?? double.PositiveInfinity);
    }
}
