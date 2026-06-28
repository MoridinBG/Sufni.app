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

A DAQ device with a GPS module configured and connected emits GPS samples in V4 SST files (as a dedicated TLV chunk) and over the live preview stream (`GpsBatch` frames). Each fix is a `GpsRecord` with timestamp, latitude/longitude, altitude, speed, fix mode and error estimates (see [V4 Data Structures](acquisition.md#v4-data-structures)). When GPS data is present the map subsystem turns those records into a polyline on a tile-map background, anchored to the session's time range; when it is not, the map surface stays hidden (recorded sessions skip track creation, live sessions only expose the map row when the accepted session header carries a non-zero GPS fix rate).

```
GpsRecord[]                     (V4 chunk in SST or live GpsBatch frame)
  -> GpsTrackPointProjection    (filter unfixed, project lon/lat -> spherical mercator)
    -> Track.FromGpsRecords     (create or null if empty)
      -> PutProcessedSessionAsync(session, track, source)
        -> `track` row, session.full_track_id, AND the cached session-window
           `session.track` polyline, all in the one processed-session transaction

Recorded session opens (pure reads — the open/load path issues no DB writes)
  -> SessionCoordinator.Load*DetailAsync
    -> TrackCoordinator.LoadSessionTrackAsync
      -> uses session.full_track_id from the snapshot (association is owned by the
         processed-write path, never by load)
      -> GetSessionTrackAsync (the cached session-window polyline)
      -> Track.GenerateSessionTrack in memory for display only when the cache is
         missing/misaligned (timestamp + session.gps_offset_seconds; NOT persisted)
        -> SessionTrackPresentationData
          -> SessionDetailViewModel.FullTrackPoints / TrackPoints
            -> MapViewModel -> MapView (Mapsui)
```

The same `MapView` Avalonia control is reused by the recorded session detail page and the live session detail page; the difference is who supplies the `TrackPoint` lists.

## Track Model

`Track` (`Sufni.App/Sufni.App/Models/Track.cs`) is a `Synchronizable` entity persisted to the `track` table. It holds an ordered list of `TrackPoint` records. Each point carries `Time`, projected `X`/`Y`, nullable `Elevation`, nullable calculated `Speed`, and optional GPS-quality metadata (`FixMode`, `Satellites`, `Epe2d`, `Epe3d`). `X`/`Y` are spherical-mercator metres (the projection Mapsui consumes natively) and `Time` is a Unix epoch in seconds. The list is exposed as a typed `Points` collection for callers and as a JSON `points` column for SQLite via a serialized companion property; cached `start_time` / `end_time` columns mirror the first and last point timestamps so the database can match a session timestamp against a track without parsing the JSON.

Two factory methods build a track:

- `Track.FromGpx(gpx)` parses a GPX 1.1 document, projects each `trkpt` from lon/lat to spherical mercator, and returns a `Track` whose points share the file's UTC timestamps. Used by the GPX import flow.
- `Track.FromGpsRecords(records)` delegates projection to `GpsTrackPointProjection.ProjectAll`, which sorts by timestamp, drops records whose `FixMode <= 0` or whose lat/lon/alt are non-finite, and projects the survivors. Used by both the SST import path and the live-capture save path.

Both factories return `null` for an empty result so the caller does not write a degenerate track row. `Track.GenerateSessionTrack(start, end)` extracts the points covering a session's time window and resamples them at 0.1-second intervals; `start` and `end` can be fractional seconds so manual alignment can shift a session window by sub-second offsets. Projected `X`/`Y` coordinates use PCHIP cubic interpolation with a linear fallback when the point count is too low; nullable elevation and speed use nullable linear interpolation. GPS-quality metadata is copied through only for samples that land on an original source timestamp. The resampled list is what `MapView` actually draws; the original `Points` list is the long polyline of the entire ride.

`GpsTrackPointProjection` (`Sufni.App/Sufni.App/Models/GpsTrackPointProjection.cs`) is the single projection helper. `TryProject(record)` returns `null` for unusable fixes; the live path uses it per-record while accumulating GPS frames so a session track can grow incrementally, and falls back to `ProjectAll` if a record arrives out of order.

`SessionSummaryMetricsCalculator` derives the compact list-summary GPS values from the same projected `TrackPoint` data, not from Mapsui. It converts finite spherical-mercator `X`/`Y` points back to lon/lat and sums haversine ground distance, so the stored distance is not distorted by map projection scale. Ascent and descent use finite elevation samples with a small hysteresis threshold so sub-threshold GPS altitude noise does not accumulate as climbing. `SessionRepository` stores those derived values on the `session` row as nullable `distance_meters`, `ascent_meters`, and `descent_meters`; it also stores `duration_seconds` from `TelemetryData.Metadata.Duration`.

## Track Coordinator

`TrackCoordinator` (`Sufni.App/Sufni.App/Coordinators/TrackCoordinator.cs`) owns two workflows:

- **GPX import** — `ImportGpxAsync` calls `IFilesService.OpenGpxFilesAsync()` to drive the platform picker, then runs `ImportGpxCoreAsync` on the background runner: parse each file, build a `Track`, skip it when `ITrackRepository.FindTrackByTimeRangeAsync` finds an active track with the same cached start/end seconds, otherwise write it through `ISynchronizableRepository<Track>.PutAsync`. The returned `GpxImportResult` lets `MainPagesViewModel.OpenGpsTracks` show an imported/already-imported notification. There is no list view for tracks — they are matched to sessions automatically by timestamp.
- **Session-track resolution (read-only)** — `LoadSessionTrackAsync(sessionId, fullTrackId, telemetryData)` runs on the background runner and returns a `SessionTrackPresentationData` (`Sufni.App/Sufni.App/SessionDetails/SessionDetailLoadModels.cs`). Opening a session is a pure read: this path never writes to the database. It uses the `fullTrackId` carried on the session snapshot — when that is null the session is unassociated and the result carries no track, because association is owned by the processed-write path (below), never by load. Otherwise it fetches the resolved `Track`, reads `session.gps_offset_seconds`, and returns the cached interpolated session track from `ISessionRepository.GetSessionTrackAsync(sessionId)` when its first point still matches `telemetry.Metadata.Timestamp + gps_offset_seconds`; when that cache is missing or misaligned it regenerates the session-window polyline with `Track.GenerateSessionTrack` **in memory for display** and returns it **without persisting** — the cached `session.track` column is (re)written only by the processed-write pipeline. The result carries the full polyline, the session-window polyline and a default `MediaColumnWidth` for the desktop layout.
- **GPS offset updates (one-way)** — `UpdateSessionGpsOffsetAsync(sessionId, fullTrackId, telemetryData, gpsOffsetSeconds)` returns `Task<bool>`. It regenerates the session-window track from the reusable full track at the requested offset and persists the new cached `session.track` plus `session.gps_offset_seconds` through `ISessionTelemetryWriter.PatchSessionTrackAsync` — a derived-only write with no optimistic-concurrency guard, so an explicit user offset can never false-conflict — then upserts the session store. The refreshed track and baseline reach the editor through the recorded-session graph's `WatchSession` reaction exactly like a recompute; the coordinator pushes no result snapshot back to the view model. `SessionDetailViewModel` drives this from the recorded plot context menu: "Mark GPS event here" starts a pending alignment mark, "Mark telemetry event here" resolves it, and the offset delta is added to the session's existing GPS offset. The actions are contributed to every built-in recorded plot row; command availability decides which action is visible at a given point in the flow.

Both `SessionCoordinator.LoadDesktopDetailAsync` and `LoadMobileDetailAsync` call `LoadSessionTrackAsync` after telemetry is in hand and pass the result through `SessionTelemetryPresentationData` / `SessionMobileLoadResult` to the detail view model. Import, recompute, and live-save persistence all get generated tracks from `RecordedSessionReprocessor`, which derives them from the processed telemetry GPS records for the current recorded source. The coordinator passes that candidate full `Track` to `ISessionTelemetryWriter.PutProcessedSessionAsync(...)`, which derives the summary metrics and the cached session-window `session.track` polyline, then runs the repository transaction that writes the track, stamps `session.FullTrack`, preserves `session.GpsOffsetSeconds`, and persists the processed session atomically with the recorded source. If a processed session has no generated track and no existing `FullTrack`, the telemetry writer links it to the active track with the **tightest** time window covering the session timestamp — `ITrackRepository.FindTrackContainingTimestampAsync` orders by `(end_time - start_time)`, then `start_time`, then `id`, so overlapping candidate windows resolve deterministically to the closest-fitting track. Because association and the cached session-window polyline are both produced here in the processed-write pipeline (import, recompute, live-save, and GPS-offset adjustment all flow through it), the open/load path never has to write either.

## Tile Layer Service

`ITileLayerService` (`Sufni.App/Sufni.App/Services/ITileLayerService.cs`) and `TileLayerService` (`Sufni.App/Sufni.App/Services/TileLayerService.cs`) own the tile-provider catalog. Registered as a singleton; `MapViewModel` (supplied by `MapViewModelFactory`) is the only consumer. The service exposes:

- `AvailableLayers` — an `ObservableCollection<TileLayerConfig>` that the map control's selector binds to.
- `SelectedLayer` — the current read-only selection.
- `SelectedLayerChanges` — an observable that emits the current selection and every subsequent change.
- `InitializeAsync()` — idempotent (cached `Task`); seeds the two built-in providers (Jawg Dark and OpenCycleMap via Thunderforest, both with embedded API keys), appends any custom layers loaded from preferences, and restores the previously selected layer.
- `SetSelectedLayerAsync` — updates the selected layer and persists the new selection through `IMapPreferences.SetSelectedLayerIdAsync`.
- `AddCustomLayerAsync` / `RemoveCustomLayerAsync` — mark the supplied `TileLayerConfig` as custom, mutate the observable collection, and persist the custom-layers list back through `IMapPreferences`.

There is no in-memory tile cache or persistent disk cache here; tile fetching is the responsibility of Mapsui's `TileLayer` and BruTile's `HttpTileSource` constructed in `MapView` from each `TileLayerConfig`. `TileLayerConfig` (`Sufni.App/Sufni.App/Models/TileLayerConfig.cs`) carries the URL template, attribution metadata, `MaxZoom`, an `IsCustom` flag and a generated `Id`.

## Map View Model

`MapViewModel` (`Sufni.App/Sufni.App/ViewModels/MapViewModel.cs`) is a thin reusable view model — neither an editor nor a list. It owns:

- `AvailableLayers` (forwarded from `ITileLayerService`).
- `SelectedLayer` — updated from `ITileLayerService.SelectedLayerChanges`; user selection is committed through `SelectLayerAsync`, which calls `ITileLayerService.SetSelectedLayerAsync`.
- `FullTrackPoints` and `SessionTrackPoints` — the two `List<TrackPoint>?` properties the host editor writes into.
- `AddCustomLayerCommand` — opens `IDialogService.ShowAddTileLayerDialogAsync` and forwards the resulting `TileLayerConfig` to the tile-layer service.

`InitializeAsync()` waits for the tile-layer service to be ready and then snapshots its current `SelectedLayer`. The view model never reads from a store or coordinator directly; the host editor (`SessionDetailViewModel` or `LiveSessionMediaWorkspaceViewModel`) creates it through `IMapViewModelFactory` (`Sufni.App/Sufni.App/ViewModels/MapViewModelFactory.cs`), kicks off `InitializeAsync`, and re-publishes its own track points into it whenever they change. The factory supplies the tile-layer service, dialog service, and UI-thread dispatcher, so the hosts no longer carry map-only constructor dependencies. This keeps the same `MapViewModel` reusable for both recorded and live detail surfaces without hard-coding either flow.

## Mapsui Integration

`MapView` (`Sufni.App/Sufni.App/Views/MapView.axaml` and `MapView.axaml.cs`) is the only place that touches Mapsui directly. The XAML is a `Mapsui.UI.Avalonia.MapControl` with a small overlay holding a tile-provider `ComboBox` and an "add custom layer" button.

The code-behind builds a fixed layer stack on construction: a tile layer (replaced when `SelectedLayer` changes), a "Full Track" `MemoryLayer` (light green polyline), a "Session Track" `MemoryLayer` (red polyline, drawn thicker), a "Start/End Marker" layer (filled circles), and a writable position-marker layer that the timeline cursor moves along the session track. When `MapViewModel.FullTrackPoints` / `SessionTrackPoints` change the view rebuilds those layers from the projected mercator coordinates with `NetTopologySuite` `LineString` geometries, then triggers a refresh.

`MapView` also exposes a `Timeline` styled property (`SessionTimelineLinkViewModel`) that the recorded and live session shells bind to. The two-way coupling lets timeline cursor and visible-range changes in one place (the graph rows, the media pane, or the map viewport) drive the others without view models depending on each other. Pointer interaction with the map computes a normalized session range from the visible viewport and pushes it back through `Timeline.SetVisibleRange`. External timeline range changes zoom the map to the matching track-point time window; if a track refresh arrives after the range was already changed, `MapView` applies the current timeline range instead of re-fitting the whole session. Straight-line or point-like selected track windows are expanded to a non-zero map extent before calling Mapsui's viewport fit so they still zoom visibly. The view's own pointer-tracking flag (`mapPointerInteractionActive`) is the gate that keeps Mapsui's own viewport-changed events from echoing back as user-driven. Desktop media XAML binds this through the shared `ISessionMediaWorkspace` contract so recorded and live desktop media surfaces use the same compiled-binding view.

For recorded-session extension overlays, `MapView` also accepts
`RecordedSessionExtensionSlots` through an `ExtensionSlots` styled
property. It renders `MapOverlays` into a dedicated Mapsui memory
layer using neutral line and point descriptors. Coordinates are
latitude/longitude in the descriptor and are projected with the same
spherical-mercator projection used for GPS tracks. The map view owns
the Mapsui feature/style translation; media workspaces only forward
the slot collection.

## Map Preferences

`IMapPreferences` (`Sufni.App/Sufni.App/Services/IAppPreferences.cs`) is a facet of `IAppPreferences`. The concrete implementation in `AppPreferences` (`Sufni.App/Sufni.App/Services/AppPreferences.cs`) writes a single JSON document — `app-preferences.json` next to the SQLite database — under a `Maps` key. Reads and writes are serialized through a `SemaphoreSlim`, and writes go through a temp-file rename so a crashed write does not corrupt the document.

The facet exposes only the operations the tile-layer service uses:

- `GetSelectedLayerIdAsync` / `SetSelectedLayerIdAsync` — the `Guid` of the last-selected `TileLayerConfig`.
- `GetCustomLayersAsync` / `SetCustomLayersAsync` — the user's custom tile-layer list (URL templates, attribution, max zoom).

DI registers `IMapPreferences` as a singleton via a factory that resolves `IAppPreferences.Map` so both interfaces point at the same backing document. `ISessionPreferences` is the sibling facet that carries per-session plot and statistics preferences; the two never share state and have no overlap.

Map preferences also participate in app-preference sync. `IAppPreferences.GetSyncDataAsync` packages the map facet as `AppPreferencesSyncData.Maps`, and `TileLayerService` refreshes its built-in/custom layer catalog and selected layer after `SyncDataApplied` so remote map-preference changes are reflected in open map views.

## Where Maps Are Displayed

The `MapView` control is hosted from three places:

- **Recorded session detail (desktop)** — `Sufni.App/Sufni.App/DesktopViews/Items/SessionMediaDesktopView.axaml` puts the map under a `PlaceholderOverlayContainer` whose grid row is star-sized from `SessionDetailViewModel.MapState`, so a track-only media column lets the map fill the available height while extension media panes remain auto-sized below it.
- **Recorded session detail (mobile)** — `Sufni.App/Sufni.App/Views/SessionPages/RecordedGraphPageView.axaml` includes the same control inside the recorded graph page's media stack.
- **Live session detail** — `Sufni.App/Sufni.App/Views/SessionPages/LiveGraphPageView.axaml` hosts the map for the mobile/shared live capture screen, while `Sufni.App/Sufni.App/DesktopViews/Editors/LiveSessionDetailDesktopView.axaml` reaches the same map through `SessionMediaDesktopView`. In both cases `LiveSessionMediaWorkspaceViewModel.MapViewModel` is the data context. The workspace gates `MapState` on `LiveSessionHeader.AcceptedGpsFixHz > 0` and shows a placeholder until the live session service produces the first projected `TrackPoint`. Each incoming `GpsBatch` frame in `LiveSessionService` projects through `GpsTrackPointProjection.TryProject` and appends to the live session's accumulated `TrackPoint[]`; the workspace re-publishes that array into `MapViewModel.SessionTrackPoints`. There is no full-track polyline during a live capture — only the session-window line is meaningful before save. On save, `SessionCoordinator.SaveLiveCaptureAsync` delegates to the recorded-session reprocessor; the reprocessor projects the captured `GpsRecord[]` through `Track.FromGpsRecords` and passes the candidate track to `PutProcessedSessionAsync`, which persists it and links it from the saved session's `full_track_id`.

The live preview's `GpsPreviewState` (`Sufni.App/Sufni.App/Services/LiveStreaming/GpsPreviewState.cs`) is independent of the map subsystem: it interprets fix-mode bytes for the diagnostics tab's status text, while `LiveSessionMediaWorkspaceViewModel` is what feeds projected points into `MapViewModel`. See [GPS Preview State](live-session.md#gps-preview-state).

The `track` SQLite table and the `session.full_track_id` linkage are documented in the [persistence schema](persistence.md#schema). `Track` is `Synchronizable`, so cross-device sync ships tracks alongside sessions through the same merge rules — see [Conflict Resolution](persistence.md#conflict-resolution).
