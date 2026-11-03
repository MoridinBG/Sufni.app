# Saved Segments

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the `Segments/` slice: saved segments of recorded sessions, their persistence and synchronization, how session windows select them, the segments picker, Strava segment efforts, and matching a range against every session.

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

A saved segment is a named range on an immutable recording source, in seconds from the source origin. Its `owner_key` is the source hash. Each session derives visibility and session-relative times from its window. A session delete never deletes segments.

The slice lives in `Sufni.App/Sufni.App/Segments/`:

- `Models/` — `SessionSegment` (table row, `Synchronizable`), the Strava result models, and the match and comparison models.
- `Services/` — `ISessionSegmentRepository`, `IStravaSegmentService`, the comparison builders, and `Matching/` (`ISegmentMatcher` and the DTW scoring).
- `Stores/` — `SessionSegmentStore` behind `ISessionSegmentStore` and `ISessionSegmentStoreWriter`.
- `Coordinators/` — `SegmentCoordinator` behind `ISegmentCoordinator`.
- `ViewModels/` — `SegmentPickerViewModel` with its factory and rows, `SegmentComparisonViewModel` with its factory and rows, `SegmentNamePromptViewModel`, and `StravaConnectDialogViewModel`.
- `Plots/` — `SegmentComparisonPlot`.
- `Views/` and `DesktopViews/` — the picker panel, the mobile bottom-sheet button, the desktop flyout button, the match toolbar, the comparison rows and plot view, the analysis comparison banner, and the two dialog views.

## Persistence

`session_segment` holds id, `owner_key`, `name`, source-relative `start_seconds` and `end_seconds`, and the `Synchronizable` columns `version`, `updated`, `updated_by`, `deleted`, `local_seq` and `sync_seq`. `ISessionSegmentRepository.GetLiveAsync` returns all non-deleted segments independently of session existence. Rename and delete change only live rows; delete soft-deletes the row and records its durable deletion ledger entry.

On older databases, the `track-materializations` migration resolves each session-owned row through its recording source. It adds the session's `window_start_seconds` to both anchors, keeps segment IDs and authored clocks, and drops `session_id`.

Startup purges segment tombstones older than one day. It keeps segments whose sessions are gone; they become visible again when a session of the same source returns.

## Store And Coordinator

`SessionSegmentStore` holds live source-owned segments. `GetForOwner` returns the segments of one source hash. `PublishSegmentsAsync` receives prepared snapshots and removal ids, publishing both in one cache update. Workflow owners classify deleted rows and prepare snapshots; the store does not inspect persistence models. `AppDataRefresher` refreshes the store with the other core stores.

`SegmentCoordinator` owns create, rename and delete, matching and comparison (see [Matching And Comparison](#matching-and-comparison)), and the Strava workflows (see [Strava](#strava)).

- **Name rule.** The coordinator alone owns the name rule: a name is required and unique among the source's live segments. `ValidateName` exposes the rule to prompts. `NextName` returns the first free "Segment N" name.
- **Create.** Create rejects a missing session and a range that is not finite or not ordered. It requires the recorded-source identity, resolved through `RecordingSourceSessionId` for a derived session. Until the source arrives, it writes nothing and returns `SourceUnavailable`; the picker asks the user to sync and retry.
- **Results.** Each workflow returns `SegmentChangeResult.Applied | SessionMissing | SourceUnavailable | SegmentMissing | Invalid | Failed`. A missing session or segment produces a notification. `Invalid` carries a fixed validation message.
- **Failures.** `Failed` carries `SegmentWriteFailureReason.Create | Rename | Delete` and separate diagnostics. The coordinator logs it once with the segment identity. The picker forwards it to the editor error bar and keeps diagnostics out of the summary.

## Synchronization

Segments travel with source-hash ownership and source-relative anchors in the `segment` list of `SynchronizationData` (protocol v6). Client and hub apply the same authored-history merge rule after sessions in the same transaction. Deletion ledger entries survive payload purge and reject older content. The shared `SynchronizationApplier` publishes affected segments with the other changed row families in one batch before the operation completes. See [sync.md](sync.md).

## Trim And Split

`SessionWindowEditPolicy` computes session windows. Trim, undo and split update those windows without changing segment rows, clocks or ownership. `SessionEditCommit` carries no segments and `SessionEditingCoordinator` publishes none. `SessionTrackTimeline` converts source-relative anchors into telemetry-relative ranges and checks containment in the current session window. A split received from a peer uses the same projection.

## Segments Picker

Each recorded-session editor creates one `SegmentPickerViewModel` through `ISegmentPickerFactory` and disposes it when the editor closes. The signals workspace exposes it as `IRecordedSessionSignalsWorkspace.SegmentPicker`. The picker observes a `SegmentPickerSessionState` stream (session timestamp, processed telemetry duration, analysis range, source hash and window start) that the editor projects from its state, and receives the analysis-range setter and the editor's error list.

- **Rows.** The saved rows derive from the store: the source's segments fully contained in the session window, ordered by source start and projected to session-relative ranges. A segment outside that range (after a trim) is hidden and comes back when an undo restores the range. The row whose range equals the analysis range (within 1 ms) is selected.
- **Actions.** Selecting a row sets the analysis range; selecting the selected row clears it. Both close the picker. Rename asks for a name; delete asks for confirmation. Both go through `ISegmentCoordinator`, and a failure goes to the editor's error list.
- **Save.** The built-in plot context-menu entry "Save current selection" (id `save-current-selection`, shown as "Analysis > Save current selection") appears on the six time-series rows and is available when the click lies inside the analysis range. It asks for a name, prefilled with the next free "Segment N", and creates the segment.
- **Name prompt.** `SegmentNamePromptViewModel` runs on the content-dialog path of `IDialogService`. It validates with `ISegmentCoordinator.ValidateName`, completes on confirm and on cancel, and exposes the confirmed name.
- **Placement.** The Segments button sits at the trailing end of the signals strip. The desktop signals view opens the panel in a flyout, and the panel holds the picker as its own data context because a hidden flyout drops the inherited one; the mobile signals page opens it as a bottom sheet sized to the window. The picker is available while processed telemetry is loaded and closes when it is gone.

## Strava

The picker lists the segment efforts of the Strava activity that overlaps the session when the user has connected their own Strava API app.

- **Storage.** `IStravaSegmentService` (singleton) keeps the client credentials and OAuth tokens in `ISecureStorage` under the `SegmentAnalysis.Strava.*` keys. Renaming them drops stored connections.
- **Efforts.** The service uses the injected `TimeProvider` for the token-refresh margin. It finds the activity with the most overlap and returns its efforts as ranges of the session's telemetry, clamped to its duration.
- **Cache.** The service caches successful effort lists per session start and duration until the connection changes. `NoMatchingActivity` and `NoUsableSegments` results expire after one minute on the injected clock.
- **Credential gate.** Credential operations share one gate. Concurrent fetches recheck stored tokens after the current refresh, and connect or reset cannot race that refresh. Disposal waits for credential work, then releases the HTTP client and the gate.
- **Coordinator.** `ISegmentCoordinator` owns `GetStravaConnectionAsync`, `CreateStravaAuthorizationUrl`, `ConnectStravaAsync`, `FindStravaSegmentsAsync` and `ResetStravaAsync`. The picker and the connect dialog call only the coordinator.
- **Failures.** The coordinator turns exceptions into `StravaConnectResult.Failed(Connect, details)` or `StravaSegmentFetchResult.Failed(Load, details)` and logs them once. Cancellation throws. The service classifies rejected HTTP responses and incomplete tokens and keeps their diagnostics out of user text. The dialog and picker show fixed summaries with collapsed details.
- **Preconditions.** Before any request, `FindStravaSegmentsAsync` returns `NotConnected` without a usable stored connection, and `MissingSessionTiming` without a session timestamp or usable telemetry duration.
- **Picker load.** The picker loads the efforts each time it opens. Closing the picker or the editor cancels the load and rejects late results, even successful ones. The request disposes its cancellation source when it settles.
- **Picker state.** One `StravaPickerState` record publishes loading, connection, rows, message and diagnostics. Selecting an effort sets only the analysis range. The picker shows the connected actions unless the result is `NotConnected` or `ReconnectRequired`.
- **Connect.** "Connect with Strava" opens `StravaConnectDialogViewModel` on the content-dialog path. The user enters the client id and secret, opens the authorization URL in the browser, and pastes the redirect URL back. The service exchanges the code for tokens.
- **Dialog lifetime.** Cancel interrupts the command. Closing the dialog host disposes the dialog and cancels pending connection work. A canceled response cannot close the dialog again or publish an error. "Reset credentials" removes every stored key and the cache.

## Matching And Comparison

The Match command compares the GPS points of the selected analysis range with the session-window track of every other session and shows the matches as child rows of the Travel, Velocity, IMU, and Speed rows.

- **Reads.** `ISessionContentReader` (`Sessions/Services/`) is the one place that joins the session, track and processed-telemetry readers for work across sessions.
- **Content token.** Every catalog item carries a `SessionContentToken`: processed-telemetry revision, selected full-track id and points revision, timestamp, duration, GPS offset, window-origin fraction and projection algorithm version. The selected track follows the shared largest-overlap/lowest-ID rule over `TrackStore`. Window points derive from the exact full-track revision.
- **Exact snapshot.** `GetExactSnapshotAsync` reads the selected content of a token. It returns `Available`, `Missing`, or `Stale` when the session or selected track changed; it never returns newer content.
- **Operation.** `ISessionOperationRunner` (`Sessions/Services/`, singleton) runs one operation per session. Starting a new one cancels the previous one. The runner ignores reports and completion of an operation that is not current. The recorded-session editor shows its state as the busy overlay and cancels it on session unload.
- **Matching.** `ISegmentCoordinator.MatchAsync` runs as the session's operation. It takes at least two source points from the session's track inside the range. `ISegmentMatcher` scores every candidate session with DTW (`SegmentDtw`) on the background runner.
- **Source check.** The request carries the source session, selected range and exact content token. The matcher rejects results if that source changed.
- **Time mapping.** Candidate timestamps, explicit fractional window origins and GPS alignment enter `SessionTrackTimeline`. Matched ranges and time maps are therefore in telemetry seconds, also for aligned tracks. Comparison track contexts use the same aligned origin.
- **Cache and order.** The matcher keeps prepared candidate paths in a 64 MB cache keyed by content. The coordinator orders matches by local segment time, then by cost; the matcher does not sort them.
- **Pages.** Comparisons are built ten matches at a time from exact snapshots of the matched sessions. A match whose session changed, is gone, or has no matched signals is skipped. `SegmentComparisonBuilder` cuts the matched travel, velocity, IMU, and speed series and the airtimes and places them on the source session's timeline.
- **State.** Each editor owns one `SegmentComparisonViewModel`, created by `ISegmentComparisonFactory` and disposed on close. It observes a `SegmentComparisonSessionState` stream (analysis range, telemetry, track and analysis settings) that the editor projects from its state.
- **State changes.** A change of the range, telemetry or track discards the matches. While a match is compared, a change of the analysis settings asks `ISegmentCoordinator.BuildAnalysisComparisonAsync` for a new comparison with the loaded detail.
- **Lifetime.** The view model owns cancellation and rejects superseded results; it does not run the builders. Unload clears it. Editor close cancels and awaits pending matching, page, detail and analysis work. Each request disposes its cancellation source when it settles.
- **Rows.** `SegmentComparisonRows` (attached to the Travel, Velocity, IMU, and Speed rows) adds one collapsed child row per comparison before the declared children. The rows have no `RowId`, so the layout preference does not store them. Each row has the match error in the title, the remove action, the compare-analysis action when the match has travel or velocity series, and the airtime toggle only when the match has airtimes (travel rows).
- **Analysis comparison.** "Compare analysis" first loads the match detail through `ISegmentCoordinator.LoadComparisonDetailAsync`. It then asks `ISegmentCoordinator.BuildAnalysisComparisonAsync`, which runs `SegmentAnalysisComparisonBuilder` on the background runner from captured comparison, detail and settings inputs. The view model applies only the result for its current match. The result is `Built | Failed(reason, details)`, and `Built` carries a `SegmentAnalysisComparison`: travel and velocity histogram overlays with statistic markers per side, and the match's damping band percentages with deltas against the current session.
- **Presentation.** The analysis pages show `SegmentAnalysisComparisonBannerView`. The travel and velocity distribution plots draw the overlays (see [plot-rendering.md](plot-rendering.md#concrete-plots)), and the damping view shows the deltas. The media workspace passes the matched track to the map (see [maps-and-tracks.md](maps-and-tracks.md)).

Matching, comparison-page reads, detail reads and analysis preparation return typed failures with `SegmentComparisonFailureReason` and optional diagnostics. The coordinator that catches the failure logs it once. The editor shows a fixed summary and separate details. Missing or changed detail content returns `Unavailable`. Cancellation and superseded results publish no failure.
