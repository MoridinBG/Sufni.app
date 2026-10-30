# Session Video

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the `Video/` slice: how a local video file is linked to recorded sessions, how the recorded-session editor plays the clip in sync with the telemetry timeline, how a clip is aligned to the telemetry, and which platform heads supply a native player.

## Table of Contents

- [Overview](#overview)
- [Persistence](#persistence)
- [Store And Coordinator](#store-and-coordinator)
- [Import](#import)
- [Clip Selection](#clip-selection)
- [Video Pane](#video-pane)
- [Playback And Timeline](#playback-and-timeline)
- [Alignment](#alignment)
- [Session List Indicator](#session-list-indicator)
- [Platform Players](#platform-players)

## Overview

A video clip is a local file path plus its recording window. The app does not copy the file. One import links the file to every recorded session whose recording window overlaps the video. Links are local to the device; they are not synchronized.

The slice lives in `Sufni.App/Sufni.App/Video/`:

- `Models/` — `SessionVideoClip` (table row), `SessionVideoSelection`, `VideoPlaybackContext`, and the player contracts `IVideoPlayerFactory`, `IVideoPlayer`, and `IVideoPlaybackBackend`.
- `Services/` — `ISessionVideoRepository` and `IVideoMetadataReader` (`Mp4VideoMetadataReader` reads the creation time and duration from the MP4 movie header).
- `Stores/` — `SessionVideoStore` behind `ISessionVideoStore` and `ISessionVideoStoreWriter`.
- `Coordinators/` — `VideoCoordinator` behind `IVideoCoordinator`.
- `ViewModels/` — `VideoPaneViewModel`, `VideoPlaybackViewModel`, and `IVideoPaneFactory`.
- `Views/` — `VideoPaneView`.

Video playback is a capability. `AppCapabilities.CanPlayVideo` is true on Windows, macOS, Linux, and iOS, and false on Android. The heads that can play video register an `IVideoPlayerFactory`.

## Persistence

`session_video` holds one row per linked session: id, `session_id`, `path`, `start_timestamp` (unix seconds), `duration_seconds`, `alignment_offset_seconds`, and `added` (unix milliseconds). One import writes one row for each matched session in a single transaction.

`DatabaseMigrationRunner` creates the table with the core tables. A clip is live only while its session is live: the repository reads the clips of live sessions, and a session delete does not touch `session_video`. Startup cleanup removes rows whose session row is gone. The private-video tables never shipped; their copy migration is removed, and the `session-video` migration marker remains recorded as applied.

## Store And Coordinator

`SessionVideoStore` holds a `SessionVideoSnapshot` per clip. The snapshot records `FileExists` when it is created, and a refresh checks the file again. `AppDataRefresher` refreshes the store with the other core stores.

`VideoCoordinator` owns the write workflows and timestamps imported links through its injected `TimeProvider`:

- `ImportAsync` picks a file through `IFilesService.OpenVideoFileAsync`, links it, and returns `VideoImportResult.Cancelled`, `Linked(sessionCount)`, or `Failed(message)`.
- `ShiftAlignmentAsync(clipId, deltaSeconds)` adds the delta to the clip's alignment offset and publishes the updated clip. It returns false when the clip no longer exists.

## Import

The built-in "Open video" action sits beside the GPX import in the desktop nav rail and the mobile side panel. `MainPagesViewModel.OpenVideoCommand` is enabled only when `CanPlayVideo` is true. It posts "Linked video to N recorded session(s)." to the selected page's notifications and posts a failure to its error messages.

The selected file must have a local path. The metadata reader runs on the background runner. A session matches when its recording window overlaps the video window; sessions without a timestamp or a positive duration never match. When no session matches, the import fails and the coordinator logs the reason for each session at debug level.

## Clip Selection

`SessionVideoSelection.Select` picks the clip to show at a telemetry time. Only clips whose file exists take part. A clip that contains the time wins over the others, and among those the latest added wins. When no clip contains the time, the nearest clip wins.

The video time of a telemetry time is `session timestamp - clip start + alignment offset + telemetry seconds`.

## Video Pane

`SessionDetailViewModel` creates its pane through `IVideoPaneFactory`. The factory returns null when no `IVideoPlayerFactory` is registered, so the editor has no pane on Android. The pane lives as long as the editor. It follows the session clips in the store and a `VideoSessionTiming` stream (session timestamp and telemetry duration) that the editor projects from its state, feeds that timing into playback, and changes `VideoUri` when the cursor moves into a different clip. `HasVideo` is true while at least one clip file exists.

`ISessionMediaWorkspace.Video` is the pane while `HasVideo` is true, otherwise null. The views template the pane only when it is not null, so no native player exists for sessions without a playable clip:

- Desktop: `SessionMediaDesktopView` has one vertical `CollapsibleSplitView` (`MediaSplit`) with the map first and the video second. The split ratio is stored under the `map` and `video` pane ids.
- Mobile: `RecordedSignalsPageView` shows the pane in a fixed-height host below the map.

`VideoPaneView` owns the native player. On attach it creates the player, loads the active clip, and hands the backend to `VideoPlaybackViewModel`. On detach it releases the backend and disposes the player. When the player cannot be created (for example, a missing native library), the view shows "Video playback is unavailable on this device." instead. The play and sync controls appear on pointer hover; the S key toggles sync.

## Playback And Timeline

`VideoPlaybackViewModel` holds play, sync, and timeline-playback state for one editor.

- Sync: while sync is on and the video is paused, the video follows the telemetry cursor. The pane sets the playback context on every cursor move; only a changed context (another clip, timing, or alignment) seeks the player.
- Timeline playback: the pane subscribes to the timeline's `PlaybackToggleRequested` (Space over a plot) and `PlaybackStopRequested` (primary click in a plot). While timeline playback is active, the video position drives the telemetry cursor and `IRecordedSessionTimeline.SetPlaybackActive(true)` stops the plots from moving the cursor on pointer input. While playback is active, the timeline pans the visible range, keeping its span, when the driven cursor lands outside it.

Sync and timeline playback need a known telemetry duration.

## Alignment

Camera clocks drift, so a clip may need an offset. Alignment uses three built-in actions in the context menu of every recorded signal row:

1. "Mark video event here" records the clip shown at the clicked telemetry time and the clicked seconds.
2. "Mark telemetry event here" ends the flow and calls `ShiftAlignmentAsync(clipId, marked seconds - clicked seconds)`.
3. "Cancel video alignment" drops the pending mark.

The editor keeps one pending `TimelineAlignmentMark` (`GpsTrack` or `VideoClip`), so only one alignment flow (GPS or video) can be active at a time.

Trim and split move the session origin: the timestamp takes the whole seconds
and the alignment offset of each clip takes the change of the fractional second,
so the formula above keeps pointing at the same video frame. A split copies the
clips to the second part with that shift applied. See
[Trim and split](ui-workflows.md#trim-and-split).

## Session List Indicator

`SessionListViewModel` joins `ISessionVideoStore` into the list projection with `LeftJoinMany`. The latest added clip of a session sets the row state: `HasPresentVideo` shows a video icon with the tooltip "video"; `HasMissingVideo` shows the same icon dimmed with the tooltip "video missing".

## Platform Players

| Head | Factory | Backend |
| --- | --- | --- |
| Windows, Linux | `LibVlcVideoPlayerFactory` in `Sufni.App/Sufni.App.Video.LibVlc/` | LibVLCSharp; the Windows head bundles the native library through `VideoLAN.LibVLC.Windows`, and the Linux head loads the installed LibVLC |
| macOS | `MacOsVideoPlayerFactory` | `AppleAvPlaybackBackend` (AVFoundation) in `Sufni.App/AppleShared/` |
| iOS | `IosVideoPlayerFactory` | `AppleAvPlaybackBackend` |

Each head registers its factory through `PlatformRoot` with its other platform services. `AppComposition` validates that player registration agrees with `CanPlayVideo`.
