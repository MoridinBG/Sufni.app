# Saved Segments

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the `Segments/` slice: saved segments of recorded sessions, their persistence and synchronization, how trim and split move them, the segments picker, Strava segment efforts, and matching a range against every session.

## Table of Contents

- [Overview](#overview)
- [Persistence](#persistence)
- [Store And Coordinator](#store-and-coordinator)
- [Synchronization](#synchronization)
- [Trim And Split](#trim-and-split)
- [Segments Picker](#segments-picker)
- [Strava](#strava)
- [Matching And Comparison](#matching-and-comparison)

## Overview

A saved segment is a named time range on one recorded session, in seconds from the session's telemetry start. A segment is live only while its session is live; that is derived when segments are read, so no delete cascades to segments.

The slice lives in `Sufni.App/Sufni.App/Segments/`:

- `Models/` — `SessionSegment` (table row, `Synchronizable`), the Strava result models, and the match and comparison models.
- `Services/` — `ISessionSegmentRepository`, `IStravaSegmentService`, the comparison builders, and `Matching/` (`ISegmentMatcher` and the DTW scoring).
- `Stores/` — `SessionSegmentStore` behind `ISessionSegmentStore` and `ISessionSegmentStoreWriter`.
- `Coordinators/` — `SegmentCoordinator` behind `ISegmentCoordinator`.
- `ViewModels/` — `SegmentPickerViewModel` with its factory and rows, `SegmentComparisonViewModel` with its factory and rows, `SegmentNamePromptViewModel`, and `StravaConnectDialogViewModel`.
- `Plots/` — `SegmentComparisonPlot`.
- `Views/` and `DesktopViews/` — the picker panel, the mobile bottom-sheet button, the desktop flyout button, the match toolbar, the comparison rows and plot view, the analysis comparison banner, and the two dialog views.

## Persistence

`session_segment` holds id, `session_id`, `name`, `start_seconds`, `end_seconds`, and the `Synchronizable` columns `updated`, `client_updated`, and `deleted`. `DatabaseMigrationRunner` creates it with the core tables and gives it the sync-delta indexes. `ISessionSegmentRepository.GetLiveAsync` returns the live segments of live sessions. Rename and delete change only live rows; delete is a soft delete.

Startup cleanup removes segment tombstones older than one day and segments whose session row no longer exists. The private-segment table never shipped; its copy migration is removed, and the `session-segment` migration marker remains recorded as applied.

## Store And Coordinator

`SessionSegmentStore` holds the live segments of live sessions. `GetForSession` returns the segments of one session. `PublishSegmentsAsync` receives prepared snapshots and removal ids, publishing both in one cache update. Workflow owners classify deleted rows and prepare snapshots; the store does not inspect persistence models. `AppDataRefresher` refreshes the store with the other core stores.

`SegmentCoordinator` owns create, rename, and delete, matching (see [Matching And Comparison](#matching-and-comparison)), and the Strava workflows (see [Strava](#strava)). It is the only place of the name rule: a name is required, and it is unique among the live segments of the session. `ValidateName` exposes the rule to prompts, and `NextName` returns the first free "Segment N" name. Create also rejects a missing session and a range that is not finite or not ordered. Each workflow returns `SegmentChangeResult.Applied | SessionMissing | SegmentMissing | Invalid | Failed`. Missing sessions/segments produce notifications; invalid input retains its fixed validation message. Write failures carry `SegmentWriteFailureReason.Create | Rename | Delete` and separate diagnostics, logged once with the segment identity. The picker forwards them to the editor error bar without embedding diagnostics in its summary.

## Synchronization

Segments travel as the `segment` list of `SynchronizationData` (protocol v5) through the core paths: the client applies remote rows verbatim, and the hub merges them by timestamp and tombstone after sessions in the same transaction. The client refreshes the store after a sync; on the hub, `InboundSyncCoordinator` refreshes it when a push carries segments. See [sync.md](sync.md).

## Trim And Split

The trim and split transactions in `SessionPersistenceTransactionRunner` move the session's segments in the same transaction (see [Trim and split](ui-workflows.md#trim-and-split)):

- Trim from beginning and undo trim shift every live segment of the session by the change of the session origin, so a segment keeps its place in the recording. A segment is never deleted by a trim.
- Split moves the segments that start at or after the click to the new second part, shifted to its origin. The other segments stay with the first part.

`SessionEditingCoordinator` publishes the changed segments with the changed sessions.

## Segments Picker

Each recorded-session editor creates one `SegmentPickerViewModel` through `ISegmentPickerFactory` and disposes it when the editor closes. The signals workspace exposes it as `IRecordedSessionSignalsWorkspace.SegmentPicker`. The picker observes a `SegmentPickerSessionState` stream (session timestamp, processed telemetry duration, analysis range) that the editor projects from its state, and receives the analysis-range setter and the editor's error list.

- **Rows.** The saved rows derive from the store: the session's segments that start at or after zero and end inside the telemetry duration, ordered by start. A segment outside that range (after a trim) is hidden and comes back when an undo restores the range. The row whose range equals the analysis range (within 1 ms) is selected.
- **Actions.** Selecting a row sets the analysis range; selecting the selected row clears it. Both close the picker. Rename asks for a name; delete asks for confirmation. Both go through `ISegmentCoordinator`, and a failure goes to the editor's error list.
- **Save.** The built-in plot context-menu entry "Save current selection" (id `save-current-selection`, shown as "Analysis > Save current selection") appears on the six time-series rows and is available when the click lies inside the analysis range. It asks for a name, prefilled with the next free "Segment N", and creates the segment.
- **Name prompt.** `SegmentNamePromptViewModel` runs on the content-dialog path of `IDialogService`. It validates with `ISegmentCoordinator.ValidateName`, completes on confirm and on cancel, and exposes the confirmed name.
- **Placement.** The Segments button sits at the trailing end of the signals strip. The desktop signals view opens the panel in a flyout, and the panel holds the picker as its own data context because a hidden flyout drops the inherited one; the mobile signals page opens it as a bottom sheet sized to the window. The picker is available while processed telemetry is loaded and closes when it is gone.

## Strava

The picker lists the segment efforts of the Strava activity that overlaps the session when the user has connected their own Strava API app.

- `IStravaSegmentService` (singleton) keeps the client credentials and OAuth tokens in `ISecureStorage` under the `SegmentAnalysis.Strava.*` keys, which predate the fold and keep existing connections. It uses injected `TimeProvider` for the token-refresh margin, finds the activity with the most overlap, and returns the efforts as ranges of the session's telemetry, clamped to its duration. Results are cached per session start and duration until the connection changes; the cache keeps negative results.
- `ISegmentCoordinator` owns the Strava workflows: `GetStravaConnectionAsync`, `CreateStravaAuthorizationUrl`, `ConnectStravaAsync`, `FindStravaSegmentsAsync`, and `ResetStravaAsync`. The picker and the connect dialog call only the coordinator. Exceptions become `StravaConnectResult.Failed(Connect, details)` or `StravaSegmentFetchResult.Failed(Load, details)` and are logged once at their first catch; cancellation throws. The service classifies rejected HTTP responses and incomplete tokens without exposing their diagnostics as user text. The dialog and picker show fixed summaries with collapsed details expanders. `FindStravaSegmentsAsync` returns `NotConnected` before any request when no usable connection is stored, and `MissingSessionTiming` when the session has no timestamp or usable telemetry duration.
- The picker loads the efforts each time it opens and passes a cancellation token; closing the picker or the editor cancels the load. Selecting an effort sets the analysis range only. The picker shows the connected actions unless the result is `NotConnected` or `ReconnectRequired`.
- "Connect with Strava" opens `StravaConnectDialogViewModel` on the content-dialog path. The user enters the client id and secret, opens the authorization URL in the browser, and pastes the redirect URL back; the service exchanges the code for tokens. "Reset credentials" removes every stored key and the cache.

## Matching And Comparison

The Match command compares the GPS points of the selected analysis range with the session-window track of every other session and shows the matches as child rows of the Travel, Velocity, IMU, and Speed rows.

- **Reads.** `ISessionContentReader` (`Sessions/Services/`) is the one place that joins the session, track, and processed-telemetry readers for work across sessions. Every catalog item carries a `SessionContentToken` (processed-telemetry revision, track projection revision, linked full-track id and points revision, timestamp, duration, and GPS offset). `GetExactSnapshotAsync` reads the selected content of a token and returns `Available`, `Stale` when the session changed since the token was taken, or `Missing`; it never returns newer content.
- **Operation.** `ISessionOperationRunner` (`Sessions/Services/`, singleton) runs one operation per session. Starting a new one cancels the previous one; reports and completion of an operation that is no longer current are ignored. The recorded-session editor renders the operation's state as its busy overlay and cancels it when the session unloads.
- **Matching.** `ISegmentCoordinator.MatchAsync` runs as the session's operation. It takes the source points from the session's track inside the range (at least two), and `ISegmentMatcher` scores every candidate session with DTW (`SegmentDtw`) on the background runner. The request always carries the source session, selected range, and exact content token; the matcher rejects results if that source has changed. It keeps prepared candidate paths in a bounded cache keyed by content (64 MB). The coordinator orders matches by local segment time, then by cost; the matcher does not sort them.
- **Pages.** Comparisons are built ten matches at a time from exact snapshots of the matched sessions. A match whose session changed, is gone, or has no matched signals is skipped. `SegmentComparisonBuilder` cuts the matched travel, velocity, IMU, and speed series and the airtimes and places them on the source session's timeline.
- **State.** Each editor owns one `SegmentComparisonViewModel` (created by `ISegmentComparisonFactory`, disposed on close). It observes a `SegmentComparisonSessionState` stream (analysis range, telemetry, track, and analysis settings) that the editor projects from its state. A change of the range, telemetry, or track discards the matches; a change of the analysis settings rebuilds only the analysis comparison. Unload clears it.
- **Rows.** `SegmentComparisonRows` (attached to the Travel, Velocity, IMU, and Speed rows) adds one collapsed child row per comparison before the declared children. The rows have no `RowId`, so the layout preference does not store them. Each row has the match error in the title, the remove action, the compare-analysis action when the match has travel or velocity series, and the airtime toggle only when the match has airtimes (travel rows).
- **Analysis comparison.** "Compare analysis" on one row builds a `SegmentAnalysisComparison` with `SegmentAnalysisComparisonBuilder`: travel and velocity histogram overlays with statistic markers per side, and the match's damping band percentages with deltas against the current session. The analysis pages show `SegmentAnalysisComparisonBannerView`, the travel and velocity distribution plots draw the overlays (see [plot-rendering.md](plot-rendering.md#concrete-plots)), the damping view shows the deltas, and the media workspace passes the matched track to the map (see [maps-and-tracks.md](maps-and-tracks.md)).
