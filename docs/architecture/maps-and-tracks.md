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
      -> databaseService.PutProcessedSessionAsync(session, track, source)
        -> `track` row + session.full_track_id in the same processed-session transaction

Recorded session opens
  -> SessionCoordinator.Load*DetailAsync
    -> TrackCoordinator.LoadSessionTrackAsync
      -> AssociateSessionWithTrackAsync (if session has no full_track_id)
      -> GetSessionTrackAsync
      -> Track.GenerateSessionTrack (cubic-spline interpolated, cached on session.track)
        -> SessionTrackPresentationData
          -> SessionDetailViewModel.FullTrackPoints / TrackPoints
            -> MapViewModel -> MapView (Mapsui)
```

The same `MapView` Avalonia control is reused by the recorded session detail page and the live session detail page; the difference is who supplies the `TrackPoint` lists.

## Track Model

`Track` (`Sufni.App/Sufni.App/Models/Track.cs`) is a `Synchronizable` entity persisted to the `track` table. It holds an ordered list of `TrackPoint` records — `(Time, X, Y, Elevation)` — where `X`/`Y` are spherical-mercator metres (the projection Mapsui consumes natively) and `Time` is a Unix epoch in seconds. The list is exposed as a typed `Points` collection for callers and as a JSON `points` column for SQLite via a serialized companion property; cached `start_time` / `end_time` columns mirror the first and last point timestamps so the database can match a session timestamp against a track without parsing the JSON.

Two factory methods build a track:

- `Track.FromGpx(gpx)` parses a GPX 1.1 document, projects each `trkpt` from lon/lat to spherical mercator, and returns a `Track` whose points share the file's UTC timestamps. Used by the GPX import flow.
- `Track.FromGpsRecords(records)` delegates projection to `GpsTrackPointProjection.ProjectAll`, which sorts by timestamp, drops records whose `FixMode <= 0` or whose lat/lon/alt are non-finite, and projects the survivors. Used by both the SST import path and the live-capture save path.

Both factories return `null` for an empty result so the caller does not write a degenerate track row. `Track.GenerateSessionTrack(start, end)` extracts the points covering a session's time window and resamples them with a 0.1-second cubic spline (`MathNet.Numerics.Interpolation.CubicSpline.InterpolatePchip`) on each axis. The resampled list is what `MapView` actually draws; the original `Points` list is the long polyline of the entire ride.

`GpsTrackPointProjection` (`Sufni.App/Sufni.App/Models/GpsTrackPointProjection.cs`) is the single projection helper. `TryProject(record)` returns `null` for unusable fixes; the live path uses it per-record while accumulating GPS frames so a session track can grow incrementally, and falls back to `ProjectAll` if a record arrives out of order.

## Track Coordinator

`TrackCoordinator` (`Sufni.App/Sufni.App/Coordinators/TrackCoordinator.cs`) owns two workflows:

- **GPX import** — `ImportGpxAsync` calls `IFilesService.OpenGpxFilesAsync()` to drive the platform picker, then runs `ImportGpxCoreAsync` on the background runner: parse each file, build a `Track`, write it through `IDatabaseService.PutAsync`. Triggered from the side panel via `MainPagesViewModel.OpenGpsTracks`. There is no list view for tracks — they are matched to sessions automatically by timestamp.
- **Session-track resolution** — `LoadSessionTrackAsync(sessionId, fullTrackId, telemetryData)` runs on the background runner and returns a `SessionTrackPresentationData` (`Sufni.App/Sufni.App/SessionDetails/SessionDetailLoadModels.cs`). If the session does not yet have a `full_track_id`, the coordinator delegates to `IDatabaseService.AssociateSessionWithTrackAsync(sessionId)`, which finds a `Track` whose `[start_time, end_time]` window straddles the session's `timestamp` and writes the link back to the `session` row. It then fetches the resolved `Track`, returns the cached interpolated session track from `GetSessionTrackAsync(sessionId)` if one exists, or generates and stores it via `Track.GenerateSessionTrack` + `PatchSessionTrackAsync`. The result carries the full polyline, the session-window polyline and a default `MapVideoWidth` for the desktop layout.

Both `SessionCoordinator.LoadDesktopDetailAsync` and `LoadMobileDetailAsync` call `LoadSessionTrackAsync` after telemetry is in hand and pass the result through `SessionTelemetryPresentationData` / `SessionMobileLoadResult` to the detail view model. `ImportSessionsCoordinator` and `SessionCoordinator.SaveLiveCaptureAsync` generate a candidate full `Track` from processed GPS data and pass it to `IDatabaseService.PutProcessedSessionAsync(...)`, which writes the track, stamps `session.FullTrack`, and persists the processed session atomically with the recorded source.

## Tile Layer Service

`ITileLayerService` (`Sufni.App/Sufni.App/Services/ITileLayerService.cs`) and `TileLayerService` (`Sufni.App/Sufni.App/Services/TileLayerService.cs`) own the tile-provider catalog. Registered as a singleton; `MapViewModel` is the only consumer. The service exposes:

- `AvailableLayers` — an `ObservableCollection<TileLayerConfig>` that the map control's selector binds to.
- `SelectedLayer` — a two-way property; the setter persists the new selection through `IMapPreferences.SetSelectedLayerIdAsync`.
- `InitializeAsync()` — idempotent (cached `Task`); seeds the two built-in providers (Jawg Dark and OpenCycleMap via Thunderforest, both with embedded API keys), appends any custom layers loaded from preferences, and restores the previously selected layer.
- `AddCustomLayerAsync` / `RemoveCustomLayerAsync` — mark the supplied `TileLayerConfig` as custom, mutate the observable collection, and persist the custom-layers list back through `IMapPreferences`.

There is no in-memory tile cache or persistent disk cache here; tile fetching is the responsibility of Mapsui's `TileLayer` and BruTile's `HttpTileSource` constructed in `MapView` from each `TileLayerConfig`. `TileLayerConfig` (`Sufni.App/Sufni.App/Models/TileLayerConfig.cs`) carries the URL template, attribution metadata, `MaxZoom`, an `IsCustom` flag and a generated `Id`.

## Map View Model

`MapViewModel` (`Sufni.App/Sufni.App/ViewModels/MapViewModel.cs`) is a thin reusable view model — neither an editor nor a list. It owns:

- `AvailableLayers` (forwarded from `ITileLayerService`).
- `SelectedLayer` — its setter pushes the choice back to `tileLayerService.SelectedLayer`, which in turn persists it.
- `FullTrackPoints` and `SessionTrackPoints` — the two `List<TrackPoint>?` properties the host editor writes into.
- `AddCustomLayerCommand` — opens `IDialogService.ShowAddTileLayerDialogAsync` and forwards the resulting `TileLayerConfig` to the tile-layer service.

`InitializeAsync()` waits for the tile-layer service to be ready and then snapshots its current `SelectedLayer`. The view model never reads from a store or coordinator directly; the host editor (`SessionDetailViewModel` or `LiveSessionMediaWorkspaceViewModel`) constructs it, kicks off `InitializeAsync`, and re-publishes its own track points into it whenever they change. This keeps the same `MapViewModel` reusable for both recorded and live detail surfaces without hard-coding either flow.

## Mapsui Integration

`MapView` (`Sufni.App/Sufni.App/Views/MapView.axaml` and `MapView.axaml.cs`) is the only place that touches Mapsui directly. The XAML is a `Mapsui.UI.Avalonia.MapControl` with a small overlay holding a tile-provider `ComboBox` and an "add custom layer" button.

The code-behind builds a fixed layer stack on construction: a tile layer (replaced when `SelectedLayer` changes), a "Full Track" `MemoryLayer` (light green polyline), a "Session Track" `MemoryLayer` (red polyline, drawn thicker), a "Start/End Marker" layer (filled circles), and a writable position-marker layer that the timeline cursor moves along the session track. When `MapViewModel.FullTrackPoints` / `SessionTrackPoints` change the view rebuilds those layers from the projected mercator coordinates with `NetTopologySuite` `LineString` geometries, then triggers a refresh.

`MapView` also exposes a `Timeline` styled property (`SessionTimelineLinkViewModel`) that the recorded and live session shells bind to. The two-way coupling lets timeline cursor and visible-range changes in one place (the graph rows, the video, or the map viewport) drive the others without view models depending on each other. Pointer interaction with the map computes a normalized session range from the visible viewport and pushes it back through `Timeline.SetVisibleRange`. The view's own pointer-tracking flag (`mapPointerInteractionActive`) is the gate that keeps Mapsui's own viewport-changed events from echoing back as user-driven.

## Map Preferences

`IMapPreferences` (`Sufni.App/Sufni.App/Services/IAppPreferences.cs`) is a facet of `IAppPreferences`. The concrete implementation in `AppPreferences` (`Sufni.App/Sufni.App/Services/AppPreferences.cs`) writes a single JSON document — `app-preferences.json` next to the SQLite database — under a `Maps` key. Reads and writes are serialized through a `SemaphoreSlim`, and writes go through a temp-file rename so a crashed write does not corrupt the document.

The facet exposes only the operations the tile-layer service uses:

- `GetSelectedLayerIdAsync` / `SetSelectedLayerIdAsync` — the `Guid` of the last-selected `TileLayerConfig`.
- `GetCustomLayersAsync` / `SetCustomLayersAsync` — the user's custom tile-layer list (URL templates, attribution, max zoom).

DI registers `IMapPreferences` as a singleton via a factory that resolves `IAppPreferences.Map` so both interfaces point at the same backing document. `ISessionPreferences` is the sibling facet that carries per-session plot and statistics preferences; the two never share state and have no overlap.

## Where Maps Are Displayed

The `MapView` control is hosted from three places:

- **Recorded session detail (desktop)** — `Sufni.App/Sufni.App/DesktopViews/Items/SessionMediaDesktopView.axaml` puts the map under a `PlaceholderOverlayContainer` that sizes the surface from `SessionDetailViewModel.MapState`.
- **Recorded session detail (mobile)** — `Sufni.App/Sufni.App/Views/SessionPages/RecordedGraphPageView.axaml` includes the same control inside the recorded graph page's media stack.
- **Live session detail** — `Sufni.App/Sufni.App/Views/SessionPages/LiveGraphPageView.axaml` does the same for the live capture screen, with `LiveSessionMediaWorkspaceViewModel.MapViewModel` as the data context. The workspace gates `MapState` on `LiveSessionHeader.AcceptedGpsFixHz > 0` and shows a placeholder until the live session service produces the first projected `TrackPoint`. Each incoming `GpsBatch` frame in `LiveSessionService` projects through `GpsTrackPointProjection.TryProject` and appends to the live session's accumulated `TrackPoint[]`; the workspace re-publishes that array into `MapViewModel.SessionTrackPoints`. There is no full-track polyline during a live capture — only the session-window line is meaningful before save. On save, `SessionCoordinator.SaveLiveCaptureAsync` re-projects the captured `GpsRecord[]` through `Track.FromGpsRecords` and passes the candidate track to `PutProcessedSessionAsync`, which persists it and links it from the saved session's `full_track_id`.

The live preview's `GpsPreviewState` (`Sufni.App/Sufni.App/Services/LiveStreaming/GpsPreviewState.cs`) is independent of the map subsystem: it interprets fix-mode bytes for the diagnostics tab's status text, while `LiveSessionMediaWorkspaceViewModel` is what feeds projected points into `MapViewModel`. See [GPS Preview State](live-session.md#gps-preview-state).

The `track` SQLite table and the `session.full_track_id` linkage are documented in the [persistence schema](persistence.md#schema). `Track` is `Synchronizable`, so cross-device sync ships tracks alongside sessions through the same merge rules — see [Conflict Resolution](persistence.md#conflict-resolution).
