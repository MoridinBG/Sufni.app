# Maps & GPS Tracks

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the map and GPS-track subsystem: how a session's GPS samples become a persisted `Track`, how the recorded and live session detail views render that track over a tile map, and where map preferences and custom tile layers are stored.

## Table of Contents

- [Overview](#overview)
- [Track Model](#track-model)
- [Track Coordinator](#track-coordinator)
- [Tile Layer Service](#tile-layer-service)
- [Map View Model](#map-view-model)
- [Mapsui Integration](#mapsui-integration)
- [Map Preferences](#map-preferences)
- [Where Maps Are Displayed](#where-maps-are-displayed)

## Overview

A DAQ device with a GPS module configured and connected emits GPS samples in V4/V5 SST files and over the live preview stream (`GpsBatch` frames). Each fix is a `GpsRecord` with timestamp, latitude/longitude, altitude, speed, fix mode and error estimates (see [Parsed Telemetry Data Structures](acquisition.md#parsed-telemetry-data-structures)). When GPS data is present the map subsystem turns those records into a polyline on a tile-map background, anchored to the session's time range; when it is not, the map surface stays hidden (recorded sessions skip track creation, live sessions only expose the map row when the accepted session header carries a non-zero GPS fix rate).

```
GpsRecord[]                     (SST GPS records or live GpsBatch frame)
  -> GpsTrackPointProjection    (filter unfixed, project lon/lat -> spherical mercator)
    -> ProcessedGeneration.TrackPoints
      -> producing coordinator creates Track when points exist
      -> ImportProcessedSessionAsync(session, track, source)
        -> full `track` row, session metadata/processed data and source
           commit together, then publish their snapshots

Recorded session opens (pure reads — the open/load path issues no DB writes)
  -> ISessionLoader.LoadDetailAsync
    -> SessionLoader decodes and validates telemetry
      -> SessionDetailViewModel publishes first Signals content
  -> the same active load generation calls ISessionLoader.LoadTrackAsync
    -> SessionLoader -> TrackCoordinator.LoadSessionTrackAsync
      -> SessionTrackSelection over TrackStore metadata and the aligned window
         (largest overlap, tie lowest content id)
      -> IFullTrackPointReader (exact id + points revision)
      -> ISessionTrackReader derives the window off-thread and caches by its inputs
        -> SessionTrackPresentationData
          -> SessionDetailViewModel.FullTrackPoints / TrackPoints
            -> MapViewModel -> MapView (Mapsui)
```

The same `MapView` Avalonia control is reused by the recorded session detail page and the live session detail page; the difference is who supplies the `TrackPoint` lists.

## Track Model

`Track` (`Sufni.App/Sufni.App/MapsAndTracks/Models/Track.cs`) is a `Synchronizable` entity persisted to the `track` table. It holds an ordered list of `TrackPoint` records. Each point carries `Time`, projected `X`/`Y`, nullable `Elevation`, nullable calculated `Speed`, and optional GPS-quality metadata (`FixMode`, `Satellites`, `Epe2d`, `Epe3d`). `X`/`Y` are spherical-mercator metres (the projection Mapsui consumes natively) and `Time` is a Unix epoch in seconds. The list is exposed as a typed `Points` collection for callers and as a JSON `points` column for SQLite via a serialized companion property; cached `start_time` / `end_time` columns preserve the fractional first and last point timestamps so the database can match a session timestamp against a track without parsing the JSON. The local-only `points_revision` column increments when the JSON payload changes and is the exact-read/cache key for full-track points; it is separate from the sync-facing `Updated` timestamp.

New generated and imported tracks use `TrackContentHash`: UUIDv5 over the
byte-stable ordered point JSON, including timestamps and nullable GPS quality.
The fixed track namespace is `3aa8c1dc-e3b2-4cd9-80b8-96a2f27ca3b0`.
Identical content has one identity across devices; a matching time range alone
is not a duplicate. Recording transactions reuse an active content track without
advancing its clock, or restore a retained deleted track atomically with the recording.

`GpxTrackReader` (`MapsAndTracks/Services/`) owns GPX 1.1 parsing. Its
`ReadAsync(PickedFile, cancellationToken)` opens the file handle, reads XML,
projects timestamped `trkpt` elements into spherical-mercator points and calculates
speed. It returns read-only points or a typed failure; malformed/empty GPX is
`InvalidFile`, file access failures are `Import`, and cancellation propagates.
It logs failures once at that boundary. `TrackCoordinator` creates and persists
the track from those points. The picker returns `PickedFile` handles (name and
read access), so no Avalonia storage object crosses into the coordinator.

Recorded reprocessing calls `GpsTrackPointProjection.ProjectAll` directly and
returns points to the producing workflow. No parsing or source factory remains
on the persisted `Track` entity.

`Track.GenerateSessionTrack(start, end)` extracts the points covering a session's time window and resamples them at 0.1-second intervals; `start` and `end` can be fractional seconds so manual alignment can shift a session window by sub-second offsets. Projected `X`/`Y` coordinates use PCHIP cubic interpolation with a linear fallback when the point count is too low; nullable elevation and speed use nullable linear interpolation. GPS-quality metadata is copied through only for samples that land on an original source timestamp. The resampled list is what `MapView` actually draws; the original `Points` list is the long polyline of the entire ride.

`SessionTrackTimeline` (`Sessions/Models/`) owns the session-clock/GPS-clock
mapping and offset normalization. Track-window projection and editor track
contexts use its aligned origin; segment matching converts GPS sample times back
to telemetry seconds through the same mapping. Video contexts derive from its
source-relative window origin, so GPS alignment does not shift video playback.
`FromSessionWindow` adds the explicit fractional part of `window_start_seconds`
to the integer timestamp for GPS-clock reads; authored GPS alignment contains no
window-origin correction. Trim/split preserve that alignment.

`GpsTrackPointProjection` (`Sufni.App/Sufni.App/MapsAndTracks/Models/GpsTrackPointProjection.cs`) is the single projection helper. `TryProject(record)` returns `null` for unusable fixes; the live path uses it per-record while accumulating GPS frames so a session track can grow incrementally, and falls back to `ProjectAll` if a record arrives out of order.

`GpsMetricsCalculator` (`MapsAndTracks/Models/`) derives distance, ascent and
descent from a projected session window. It converts finite spherical-mercator
coordinates back to lon/lat for haversine ground distance and applies the existing
three-metre elevation hysteresis. Algorithm version 1 participates in the metrics
key. Duration remains part of processed session metadata; it is normalized by
`SessionDuration` independently of GPS metrics.

`TrackStore` exposes metadata snapshots (content ID, fractional bounds and points
revision) through read-only and writer interfaces. Full refresh loads these
without point JSON. Producing workflows load the committed metadata before
publication, so database-assigned revisions reach the store. Session/source/track
changes from one recording commit publish in one batch; deletion publishes the
track ID returned by the same transaction that removed the session.

## Track Coordinator

`TrackCoordinator` (`Sufni.App/Sufni.App/MapsAndTracks/Coordinators/TrackCoordinator.cs`) owns the following workflows:

- **GPX import** — `ImportGpxAsync` calls `IFilesService.OpenGpxFilesAsync()` to drive the platform picker, then runs `ImportGpxCoreAsync` on the background runner: ask `IGpxTrackReader` for each file's projected points, build a `Track`, skip it when `ISynchronizableRepository<Track>.GetAsync` finds its active content ID, otherwise write it through `ISynchronizableRepository<Track>.PutAsync` and publish its persisted metadata to `TrackStore`. The returned `GpxImportResult` is `Canceled | Completed | Failed`; completion and failure carry imported/already-imported counts. Import stops on the first failed file, preserving earlier writes. Invalid XML or a file without timestamped track points returns `GpxImportFailureReason.InvalidFile`; picker, I/O and persistence failures return `Import`. The reader logs parsing/read failures once; the coordinator logs picker/persistence failures. Shell feedback displays the completed counts and fixed failure summary with optional diagnostics. There is no list view for tracks — they are matched to sessions automatically by timestamp.
- **Session-track resolution (read-only)** — `LoadSessionTrackAsync(sessionId, telemetryData)` reads the session and selects from `TrackStore` using `SessionTrackSelection`: greatest positive time overlap with the aligned session window, tie lowest ID. It reads exact full-track points and derives the window through `ISessionTrackReader`. A newly imported fuller GPX becomes selected on the next read without a session write. Missing timestamps/durations use the already-loaded telemetry metadata for this presentation path.

`SessionTrackReader` caches by `(track id, points revision, timestamp, duration,
GPS offset, window-origin fraction, projection version)`. Its background job reads exact full-track points
and applies the shared timeline/window projection. It has no session-store
subscription, repository read, alignment-validation fallback or latest-revision
retry. Both point readers keep their independent 64-entry/145,000-point LRU
budgets; an oversized value is shared with current waiters without retention.
A missing exact revision returns no projection. `SessionContentReader` uses the
same selection rule for catalog tokens, loads only requested payload families,
and revalidates the token after the load, including a changed track selection.
`session.track` and `track_projection_revision` are absent from the schema and
wire. `full_track_id` is also absent: track selection is derived from the committed
track catalog and session window. The recorded-session domain projection includes
the selected metadata, so an arrival, removal or points change refreshes an open
editor without a session write or a draft-discard prompt.

`BuildAnalysisComparisonAsync` also owns background execution of the segment
analysis comparison builder from captured comparison/detail/settings inputs. The
editor requests it with its current cancellation token and applies only its current
result. Comparison runtime failure handling remains in its planned runtime migration.

`TrackCoordinator` owns the device-local GPS metric materialization. Committed
session/track publications cause it to capture immutable metadata and evaluate
`SessionGpsMetricsQuery` off-thread. The key contains selected track content ID,
timestamp, duration, GPS offset, fractional window origin, track projection version
and metrics algorithm version. Local points revisions affect exact reads but never
metric identity. A stored-key mismatch starts one computation for that session;
a changed key supersedes its old work. No other workflow requests this recompute.

The worker derives the window and computes metrics off-thread. `ITrackRepository`
repeats the current-key comparison inside the SQLite transaction before writing
`session_gps_metrics`. Publication rechecks the current inputs and changes only the
metric fields/key on the current session snapshot, preserving intervening authored
metadata. Existing metrics remain visible until replacement, with no pending state.
An empty available window produces empty metrics with a current key; an unavailable
exact point payload retains the previous materialization. Failed work logs once
and leaves its key stale. The next relevant commit or startup can retry it.

The singleton owns its commit subscription, coalesced input evaluation and all
current/displaced metric tasks. Startup resolves it before server activity;
`AppShutdown` stops intake, cancels work and awaits drainage before disposal.

GPS alignment belongs to `SessionCoordinator.ShiftGpsOffsetAsync(sessionId,
deltaSeconds)`. It reads the persisted session and current processed telemetry, adds the delta
to the stored offset, selects the full track for that aligned window through the
same overlap rule, and checks that the window contains points. It persists only
the authored offset, then publishes the session snapshot. The metrics owner reacts
to that new input independently. `GpsOffsetResult` distinguishes `Applied`,
`NoMatchingTrack`, `TelemetryUnavailable`, `SessionMissing` and `Failed`. The editor sends only the delta
between its GPS and telemetry marks; the watch reaction refreshes the displayed
track. Alignment is authored state and syncs immediately with session metadata.
Metadata draft saves and processed-generation swaps preserve it.

`ISessionLoader.LoadDetailAsync` returns decoded, validated telemetry without waiting for track resolution, so `SessionDetailViewModel` can publish the first usable Signals state. The same active load token then calls `ISessionLoader.LoadTrackAsync`; a current completion augments that loaded state with `SessionTrackPresentationData`, while cancellation, unload, or a newer load prevents the late track result from publishing. A track failure settles as `Loaded` with a typed `TrackFailure` on the detail data: telemetry, analysis and the selected range remain usable, while the map shows a fixed unavailable message. Diagnostic details stay in the editor’s expandable error feedback. A successful later load clears the map failure. Import, recompute and live-save receive projected GPS points in the reprocessor's pure `ProcessedGeneration`. Each workflow creates a content-addressed candidate and commits it with the session/source through `ISessionRepository`, then publishes the committed track metadata. No workflow writes a session-to-track association. Recompute conditionally deletes the previously selected track after its commit; session deletion selects its candidate and checks remaining references within the deletion transaction. Both use the common selection rule over all active sessions: overlap alone is not a reference when a session selects a different track.

## Tile Layer Service

`ITileLayerService` and `TileLayerService` derive one `TileLayerState` from the
built-in providers, `CustomTileLayerStore`, and device-local selected layer ID.
`Observe()` replays the catalog and selection together. Store arrivals update
available layers without changing the device selection. A missing selected id falls back to the first built-in
layer. Publication runs through the required UI dispatcher. The singleton starts its derivation at construction and owns/disposes its input subscription. No startup or editor initialization call is needed.

`IMapLayerCoordinator` / `MapLayerCoordinator` own `AddAndSelectAsync` and
`SelectAsync`. They persist custom layers through `ISynchronizableRepository<CustomTileLayer>`,
publish committed snapshots, and persist the device selection through preferences.
Writes run through the background runner and are serialized so concurrent additions
preserve both layers. Provider
disposal waits for accepted writes to finish. Adding a
layer saves the catalog before selecting it. Failed catalog writes leave the
visible state unchanged; failed selection retains the saved layer and previous
selection. `MapLayerWriteResult.Saved | Failed(reason, details)` distinguishes
`MapLayerFailureReason.Add | Select`. Failures log once with the layer id and
operation. The coordinator never edits the derived tile state.

There is no in-memory tile cache or persistent disk cache here; tile fetching is the responsibility of Mapsui's `TileLayer` and BruTile's `HttpTileSource` constructed in `MapView` from each `TileLayerConfig`. `TileLayerConfig` (`Sufni.App/Sufni.App/MapsAndTracks/Models/TileLayerConfig.cs`) carries the URL template, attribution metadata, `MaxZoom`, an `IsCustom` flag and a generated `Id`.

## Map View Model

`AddTileLayerViewModel` owns the custom-layer draft and confirmation. The map
opens it through `IDialogService.ShowContentDialogAsync`; generic window/overlay
hosting lives in `Shared/Views/Dialogs/DialogService`. Cancel and window dismissal
leave the draft unaccepted. Existing window dimensions and overlay spacing stay
with the dialog options.

`MapViewModel` is a reusable presentation over the tile service's current
`TileLayerState`; `AvailableLayers` and `SelectedLayer` derive from that one
snapshot. `SelectLayerAsync` forwards the id to the map-layer coordinator.
`AddCustomLayerCommand` opens `IDialogService.ShowAddTileLayerDialogAsync` and
forwards the draft to `AddAndSelectAsync`. Fixed failure summaries and optional
diagnostics appear in the map's shared error bar. A failed selection restores
the selector to the published value.

`FullTrackPoints`, `SessionTrackPoints` and `TimelineContext` remain host-supplied
map inputs. `MapViewModelFactory` supplies the tile service, coordinator, dialog,
dispatcher, clock and background geometry factory. The view uses that factory for its
rendered resources; the view model does not build native geometry. Both recorded and live hosts use it and dispose the map
view model with their lifetime; disposal releases the tile-state subscription.
The replayed tile snapshot supplies initial state without a separate initialization method.

## Mapsui Integration

`MapView` (`Sufni.App/Sufni.App/MapsAndTracks/Views/MapView.axaml` and `MapView.axaml.cs`) is the only place that touches Mapsui directly. The XAML hosts a view-lifetime `Mapsui.UI.Avalonia.MapControl` with a small overlay holding a tile-provider `ComboBox` and an "add custom layer" button.

`App.Initialize` sets Mapsui logging-widget policy once during composition.
After each `MapControl` construction, `MapView` replaces Mapsui's static
browser-opening callback with a control-independent handler. Inspection of the
installed Mapsui.UI.Avalonia 5.1.0 assembly confirms `SharedConstructor` still
assigns the instance-bound callback, so the post-construction workaround remains.
The view creates a fresh control/map on attach, and on detach cancels geometry work,
unsubscribes its timeline and disposes the map/control and every layer. Mapsui 5.1
map disposal unsubscribes layers and aborts fetches but does not dispose layers;
`MapView` explicitly removes and disposes them. Tile replacement also disposes the
removed tile layer.

The code-behind builds a fixed layer stack on each attach: a tile layer (replaced when `SelectedLayer` changes), a "Full Track" `MemoryLayer` (light green polyline), a "Session Track" `MemoryLayer` (red polyline, drawn thicker), a "Start/End Marker" layer (filled circles), and a writable position-marker layer that the timeline cursor moves along the session track. When `MapViewModel.FullTrackPoints` / `SessionTrackPoints` or the comparison changes,
the view captures a `MapGeometryInput` and requests `IMapGeometryFactory` on the
background runner. The returned snapshot contains line/marker features and a
`TrackPointTimeIndex`; unchanged point-list references reuse prior geometry.
Only the current attached request applies its result on the UI thread. Cursor and
viewport changes consume the prepared index without building geometry. A failed
preparation logs once at the view boundary and shows fixed feedback with separate
diagnostics. Data-context changes clear the prior source's geometry.

`MapView` also exposes a `Timeline` styled property (`SessionTimelineLinkViewModel`) that the recorded and live session shells bind to. The two-way coupling lets timeline cursor and visible-range changes in one place (the signal rows, the media pane, or the map viewport) drive the others without view models depending on each other. Pointer interaction with the map computes a normalized session range from the visible viewport and pushes it back through `Timeline.SetVisibleRange`. External timeline range changes zoom the map to the matching track-point time window; if a track refresh arrives after the range was already changed, `MapView` applies the current timeline range instead of re-fitting the whole session. Cursor lookup and timeline-window extraction use `TrackPointTimeIndex`, which sorts finite point times once and performs binary-search closest-point/range queries while preserving boundary neighbors. Viewport-to-range scanning still walks visible geometry because it depends on screen-space intersection. Straight-line or point-like selected track windows are expanded to a non-zero map extent before calling Mapsui's viewport fit so they still zoom visibly. The view's own pointer-tracking flag (`mapPointerInteractionActive`) is the gate that keeps Mapsui's own viewport-changed events from echoing back as user-driven. Desktop media XAML binds this through the shared `ISessionMediaWorkspace` contract so recorded and live desktop media surfaces use the same compiled-binding view.

`MapView` also accepts a `MatchedTrack` styled property
(`MatchedTrackOverlay`: the session's points of a compared segment match and
the match's points). It draws both as lines on a "Matched Track" memory layer
above the session track, in the comparison color: the session part thinner and
faint, the match thicker. The recorded media workspace forwards the analysis
comparison of the segment comparison state (see
[segments.md](segments.md#matching-and-comparison)); live sessions have none.

## Map Preferences

`IMapPreferences` (`Sufni.App/Sufni.App/Shell/Services/Preferences/IAppPreferences.cs`) is a facet of `IAppPreferences`. The concrete implementation in `AppPreferences` (`Sufni.App/Sufni.App/Shell/Services/Preferences/AppPreferences.cs`) writes a single JSON document — `app-preferences.json` next to the SQLite database — under a `maps` key. Reads and writes are serialized through a `SemaphoreSlim`, and writes go through a temp-file rename so a crashed write does not corrupt the document.

The facet exposes the map coordinator’s writes and tile service’s observation:

- `GetSelectedLayerIdAsync` / `SetSelectedLayerIdAsync` — the `Guid` of the last-selected `TileLayerConfig`.
- `ObserveChanges()` — replayed selected ID and subsequent committed local changes, published on the UI dispatcher.

Map owners obtain the map facet from the shared `IAppPreferences` instance. `ISessionPreferences` is the sibling facet that carries per-session plot and analysis preferences; the two never share state and have no overlap.

Custom definitions live in the synced `custom_tile_layer` table with ordinary
version/writer/sequence metadata and a deletion ledger. `IsCustom` is derived
when composing the catalog. The `sync-authority` migration copies the launch
snapshot's legacy custom layers once; startup rewrites the preference document
only after that database migration commits. Selection, theme, analysis, plot and
layout settings stay device-local and have no sync payload or clock.

## Where Maps Are Displayed

The `MapView` control is hosted from three places:

- **Recorded session detail (desktop)** — `Sufni.App/Sufni.App/Sessions/Media/DesktopViews/Items/SessionMediaDesktopView.axaml` puts the map under a `PlaceholderOverlayContainer` (bound to `MapState`) in the first pane of the vertical `MediaSplit`; the video pane fills the second pane when the session has a playable clip (see [video.md](video.md)), and otherwise the map takes the full height.
- **Recorded session detail (mobile)** — `Sufni.App/Sufni.App/Sessions/Signals/Views/SessionPages/RecordedSignalsPageView.axaml` includes the same control inside the recorded Signals page's media stack, above the video pane host.
- **Live session detail** — `Sufni.App/Sufni.App/LiveDaq/Views/SessionPages/LiveSignalsPageView.axaml` hosts the map for the mobile/shared live capture screen, while `Sufni.App/Sufni.App/LiveDaq/DesktopViews/Editors/LiveSessionDetailDesktopView.axaml` reaches the same map through `SessionMediaDesktopView`. In both cases `LiveSessionMediaWorkspaceViewModel.MapViewModel` is the data context. The workspace gates `MapState` on `LiveSessionHeader.AcceptedGpsFixHz > 0` and shows a placeholder until the live session service produces the first projected `TrackPoint`. Each incoming `GpsBatch` frame in `LiveSessionService` projects through `GpsTrackPointProjection.TryProject` and appends to the live session's accumulated `TrackPoint[]`; the workspace re-publishes that array into `MapViewModel.SessionTrackPoints`. There is no full-track polyline during a live capture — only the session-window line is meaningful before save. On save, `LiveDaqCoordinator.SaveCaptureAsync` delegates to the recorded-session reprocessor; the reprocessor projects the captured `GpsRecord[]` through `GpsTrackPointProjection.ProjectAll` and returns pure points. The coordinator creates the candidate track and passes it to `ImportProcessedSessionAsync`, which persists it with the recording. The shared overlap rule selects it on read.

The live preview's `GpsPreviewState` (`Sufni.App/Sufni.App/LiveDaq/Services/LiveStreaming/GpsPreviewState.cs`) is independent of the map subsystem: it interprets fix-mode bytes for the diagnostics tab's status text, while `LiveSessionMediaWorkspaceViewModel` is what feeds projected points into `MapViewModel`. See [GPS Preview State](live-session.md#gps-preview-state).

The independent `track` SQLite table is documented in the [persistence schema](persistence.md#schema). `Track` is `Synchronizable`, so cross-device sync ships tracks alongside sessions through the same merge rules — see [Conflict Resolution](persistence.md#conflict-resolution).
