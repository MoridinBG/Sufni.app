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

A video clip is a local file path plus its recording window. The app does not copy the file. One import links the file once per recording source whose session window overlaps the video. Every session window over that source derives its visible clips from the shared link. Links are local to the device; they are not synchronized.

The slice lives in `Sufni.App/Sufni.App/Video/`:

- `Models/` — `SessionVideoClip` (table row), `SessionVideoSelection`, `VideoPlaybackContext`, and the player contracts `IVideoPlayerFactory`, `IVideoPlayer`, and `IVideoPlaybackBackend`.
- `Services/` — `ISessionVideoRepository` and `IVideoMetadataReader` (`Mp4VideoMetadataReader` reads the creation time and duration from the MP4 movie header).
- `Stores/` — `SessionVideoStore` behind `ISessionVideoStore` and `ISessionVideoStoreWriter`.
- `Coordinators/` — `VideoCoordinator` behind `IVideoCoordinator`.
- `ViewModels/` — `VideoPaneViewModel`, `VideoPlaybackViewModel`, and `IVideoPaneFactory`.
- `Views/` — `VideoPaneView`.

Video playback is a capability. `AppCapabilities.CanPlayVideo` is true on Windows, macOS, Linux, and iOS, and false on Android. The heads that can play video register an `IVideoPlayerFactory`.

## Persistence

`session_video` holds one row per source and file: id, `owner_key` (source hash), `path`, `start_timestamp` (file-header Unix seconds), `duration_seconds`, owner-relative `alignment_offset_seconds`, and `added` (Unix milliseconds). A new link's offset is the source timestamp minus the video start timestamp. Existing links retain their ID, added time and alignment on re-import.

The `track-materializations` migration converts old session offsets to the source timeline, groups links by source hash and path, and retains the widest session's alignment. An existing source-owning parent represents the pre-split full recording; equal candidates use the lowest clip ID. It logs dropped clip/session identities, preserves the survivor's ID and added time, and drops `session_id`. Reads and startup cleanup retain links independently of session existence. There is no session-ID ownership fallback. The private-video tables never shipped; their copy migration is removed, and the `session-video` marker remains recorded as applied.

## Store And Coordinator

Full clip refreshes are loaded and mapped by `VideoCoordinator.RefreshAsync`
through its background runner, then published through the store's
`ReplaceAllAsync`. Startup prepares the same snapshots in `AppDataRefresher`'s
whole-app batch; the store itself has no repository dependency.


`SessionVideoStore` holds a `SessionVideoSnapshot` per clip and accepts prepared
snapshots only. Import and alignment prepare clip snapshots
off-thread, including the file-presence check, before publication. The snapshot records `FileExists` when it is created, and a refresh checks the file again. `AppDataRefresher` refreshes the store with the other core stores.

`VideoCoordinator` owns the write workflows and timestamps imported links through its injected `TimeProvider`:

- `ImportAsync` picks a file through `IFilesService.OpenVideoFileAsync`, links each source once, and returns `VideoImportResult.Cancelled`, `Linked(sessionCount)`, `NoMatchingSession`, `SourceUnavailable`, or `Failed(reason, details)`.
- `ShiftAlignmentAsync(clipId, deltaSeconds)` adds the delta to the clip's alignment offset and publishes the updated clip. It returns `VideoAlignmentResult.Updated`, `ClipMissing`, or `Failed(reason, details)`. The coordinator catches and logs persistence/publication failures once; the pane forwards the typed outcome. The editor projects a fixed save-failure sentence with separate diagnostics, and treats a missing clip as an unavailable metadata notification.

## Import

The built-in "Open video" action sits beside the GPX import in the desktop nav rail and the mobile side panel. `MainPagesViewModel.OpenVideoCommand` is enabled only when `CanPlayVideo` is true. `ShellFeedbackViewModel` posts "Linked video to N recorded session(s)." globally and maps typed failures to fixed text.

The selected file must have a local path. The metadata reader runs on the background runner. A session matches when its recording window overlaps the video window; sessions without a timestamp or a positive duration never match. Before linking, the coordinator requires recorded-source identities for every matching session. A missing source defers the whole import without writing links; `SourceUnavailable` asks the user to sync and retry. There is no queued import or session-ID fallback. When no session matches, the import returns `NoMatchingSession` and the shell displays a notification. The coordinator logs the reason for each session at debug level. Invalid metadata returns the `InvalidMetadata` reason with diagnostic details; file/persistence exceptions return `Import`, and a non-local selection returns `LocalFileRequired`. Parser exceptions are logged once by the metadata reader; other import exceptions are logged by the coordinator.

## Clip Selection

`SessionVideoSelection.Select` picks the clip to show at a telemetry time. Only clips whose file exists take part. A clip that contains the time wins over the others, and among those the latest added wins. When no clip contains the time, the nearest clip wins.

Visible clips share the source hash and overlap the session's source-relative window. The video time is `window start + owner alignment offset + telemetry seconds`; `SessionTrackTimeline` owns the window mapping.

## Video Pane

`SessionDetailViewModel` creates its pane through `IVideoPaneFactory`. The factory returns null when no `IVideoPlayerFactory` is registered, so the editor has no pane on Android. The pane lives as long as the editor. It follows source-owned clips in the store and a `VideoSessionTiming` stream (source hash, window start and telemetry duration) that the editor projects from its state, feeds that timing into playback, and changes `VideoUri` when the cursor moves into a different clip. `HasVideo` is true while at least one clip file exists.

`ISessionMediaWorkspace.Video` is the pane while `HasVideo` is true, otherwise null. The views template the pane only when it is not null, so no native player exists for sessions without a playable clip:

- Desktop: `SessionMediaDesktopView` has one vertical `CollapsibleSplitView` (`MediaSplit`) with the map first and the video second. The split ratio is stored under the `map` and `video` pane ids.
- Mobile: `RecordedSignalsPageView` shows the pane in a fixed-height host below the map.

`VideoPaneView` owns the native player. On attach it creates the player, loads the active clip, and hands the backend to `VideoPlaybackViewModel`. On detach it releases the backend and disposes the player. When the player cannot be created (for example, a missing native library), the view shows "Video playback is unavailable on this device." instead. The play and sync controls appear on pointer hover; the S key toggles sync.

## Playback And Timeline

`VideoPlaybackViewModel` holds play, sync, and timeline-playback state for one editor.
`VideoPlaybackContext` derives the session offset from `SessionTrackTimeline`'s
source-relative window origin and owns both playback-time conversions. Clip selection and
playback share that mapping; GPS alignment changes the map clock only.

- Sync: while sync is on and the video is paused, the video follows the telemetry cursor. The pane sets the playback context on every cursor move; only a changed context (another clip, timing, or alignment) seeks the player.
- Timeline playback: the pane subscribes to the timeline's `PlaybackToggleRequested` (Space over a plot) and `PlaybackStopRequested` (primary click in a plot). While timeline playback is active, the video position drives the telemetry cursor and `IRecordedSessionTimeline.SetPlaybackActive(true)` stops the plots from moving the cursor on pointer input. While playback is active, the timeline pans the visible range, keeping its span, when the driven cursor lands outside it.

Sync and timeline playback need a known telemetry duration.

## Alignment

Camera clocks drift, so a clip may need an offset. Alignment uses three built-in actions in the context menu of every recorded signal row:

1. "Mark video event here" records the clip shown at the clicked telemetry time and the clicked seconds.
2. "Mark telemetry event here" ends the flow and calls `ShiftAlignmentAsync(clipId, marked seconds - clicked seconds)`.
3. "Cancel video alignment" drops the pending mark.

The editor keeps one pending `TimelineAlignmentMark` (`GpsTrack` or `VideoClip`), so only one alignment flow (GPS or video) can be active at a time.

Trim and split change only session windows; video rows and their alignment remain fixed. Each window derives its visible clips and playback offset from the source timeline, including a split received from a peer without a local split command. See [Trim and split](ui-workflows.md#trim-and-split).

## Session List Indicator

`RecordedSessionProjection` observes committed session/source/video inputs and derives the latest added clip overlapping each session window off-thread. Its summary carries that clip to `SessionListViewModel`, which only renders the row state: `HasPresentVideo` shows a video icon with the tooltip "video"; `HasMissingVideo` shows the same icon dimmed with the tooltip "video missing".

## Platform Players

| Head | Factory | Backend |
| --- | --- | --- |
| Windows, Linux | `LibVlcVideoPlayerFactory` in `Sufni.App/Sufni.App.Video.LibVlc/` | LibVLCSharp; the Windows head bundles the native library through `VideoLAN.LibVLC.Windows`, and the Linux head loads the installed LibVLC |
| macOS | `MacOsVideoPlayerFactory` | `AppleAvPlaybackBackend` (AVFoundation) in `Sufni.App/AppleShared/` |
| iOS | `IosVideoPlayerFactory` | `AppleAvPlaybackBackend` |

Each head registers its factory through `PlatformRoot` with its other platform services. `AppComposition` validates that player registration agrees with `CanPlayVideo`.
