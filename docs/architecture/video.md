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

A video clip is a local file path plus its recording window. The app does not copy the file. One import links the file once per recording source whose session window overlaps the video. Each session window over that source derives its visible clips from the shared link. Links are local to the device and are not synchronized.

The slice lives in `Sufni.App/Sufni.App/Video/`:

- `Models/` — `SessionVideoClip` (table row), `SessionVideoSelection`, `VideoPlaybackContext`, and the player contracts `IVideoPlayerFactory`, `IVideoPlayer`, and `IVideoPlaybackBackend`.
- `Services/` — `ISessionVideoRepository` and `IVideoMetadataReader` (`Mp4VideoMetadataReader` reads the creation time and duration from the MP4 movie header).
- `Stores/` — `SessionVideoStore` behind `ISessionVideoStore` and `ISessionVideoStoreWriter`.
- `Coordinators/` — `VideoCoordinator` behind `IVideoCoordinator`.
- `ViewModels/` — `VideoPaneViewModel`, `VideoPlaybackViewModel`, and `IVideoPaneFactory`.
- `Views/` — `VideoPaneView`.

Video playback is a capability. `AppCapabilities.CanPlayVideo` is true on Windows, macOS, Linux, and iOS, and false on Android. The heads that can play video register an `IVideoPlayerFactory`.

## Persistence

`session_video` holds one row per source and file: id, `owner_key` (source hash), `path`, `start_timestamp` (file-header Unix seconds), `duration_seconds`, owner-relative `alignment_offset_seconds`, and `added` (Unix milliseconds). A new link's offset is the source timestamp minus the video start timestamp. A re-import keeps the link's ID, added time and alignment. Reads and startup cleanup keep links whether or not a session exists.

On older databases, the `track-materializations` migration moves session-owned links to the source timeline and drops `session_id`. For each source hash and path, it keeps the source-owning parent's link, else the widest session's link, else the lowest clip ID, and logs each dropped link.

## Store And Coordinator

`ISessionVideoRepository` prepares clip snapshots at load, link and alignment.
It reads file presence through `IVideoFilePresence`, which runs on the background runner.
`SessionVideoSnapshot.From` is a pure mapping with explicit presence. Startup
publishes those snapshots in `AppDataRefresher`'s whole-app batch.

`SessionVideoStore` keeps every source-owned row, including missing files and
owners with no current session. Session-window projections decide visibility.
The application-lived `VideoCoordinator` observes the distinct linked paths
through `IVideoFilePresence.Observe`. A presence change updates every matching clip in one
publication. `VideoFilePresence` checks every five seconds on the injected clock and emits only changes.
Disposal of the coordinator stops the observation and cancels queued publication. Editors have no clip-refresh command.

`VideoCoordinator` owns the write workflows and timestamps imported links through its injected `TimeProvider`:

- `ImportAsync` receives a prepared file from `IVideoImportFileService`, links each source once, and returns `VideoImportResult.Cancelled`, `Linked(sessionCount)`, `NoMatchingSession`, `SourceUnavailable`, or `Failed(reason, details)`.
- `ShiftAlignmentAsync(clipId, deltaSeconds)` adds the delta to the clip's alignment offset and publishes the updated clip. It returns `VideoAlignmentResult.Updated`, `ClipMissing`, or `Failed(reason, details)`.
- The coordinator logs persistence and publication failures once. The pane forwards the typed outcome. The editor shows a fixed save-failure sentence with separate diagnostics, and shows a missing clip as a notification.

## Import

The "Open video" action sits beside the GPX import in the desktop nav rail and the mobile side panel. `MainPagesViewModel.OpenVideoCommand` is enabled only when `CanPlayVideo` is true. `ShellFeedbackViewModel` posts "Linked video to N recorded session(s)." and maps typed failures to fixed text.

- **File.** `VideoImportFileService` owns the picker selection. It reads metadata and saves file access on the background runner, then disposes the selection. The selected file must have a local path.
- **Matching.** A session matches when its recording window overlaps the video window. A session without a timestamp or a positive duration does not match.
- **Sources.** The coordinator requires a recorded-source identity for every matching session. If one is missing, it writes no links and returns `SourceUnavailable`, which asks the user to sync and retry.
- **No match.** The import returns `NoMatchingSession`, and the shell shows a notification. The coordinator logs the reason for each session at debug level.
- **Failures.** A non-local selection returns `LocalFileRequired`. Invalid metadata returns `InvalidMetadata` with diagnostic details. A failed persistent file grant returns `Access`. File and persistence exceptions return `Import`.
- **Logging.** The metadata reader logs parser exceptions, the file service logs picker and read failures, and the coordinator logs persistence failures. Each failure is logged once.

## Clip Selection

`SessionVideoSelection.Select` picks the clip to show at a telemetry time. Only clips whose file exists take part. A clip that contains the time wins over the others, and among those the latest added wins. When no clip contains the time, the nearest clip wins.

Visible clips share the source hash and overlap the session's source-relative window. The video time is `window start + owner alignment offset + telemetry seconds`; `SessionTrackTimeline` owns the window mapping.

## Video Pane

`SessionDetailViewModel` creates its pane through `IVideoPaneFactory`. The factory returns null when no `IVideoPlayerFactory` is registered, so the editor has no pane on Android. The pane lives as long as the editor. It follows source-owned clips in the store and a `VideoSessionTiming` stream (source hash, window start and telemetry duration) that the editor projects from its state. It feeds that timing into playback and changes `VideoUri` when the cursor moves into a different clip. `HasVideo` is true while at least one clip file exists.

`ISessionMediaWorkspace.Video` is the pane while `HasVideo` is true, otherwise null. The views template the pane only when it is not null, so no native player exists for sessions without a playable clip:

- Desktop: `SessionMediaDesktopView` has one vertical `CollapsibleSplitView` (`MediaSplit`) with the map first and the video second. The split ratio is stored under the `map` and `video` pane ids.
- Mobile: `RecordedSignalsPageView` shows the pane in a fixed-height host below the map.

`VideoPaneView` owns the native player:

- On attach, it creates the player, loads the active clip, and hands the prepared backend to `VideoPlaybackViewModel`. On detach, it releases the backend and disposes the player.
- When the player cannot be created (for example, a missing native library), the view shows "Video playback is unavailable on this device."
- The play and sync controls appear on pointer hover. The S key toggles sync.
- One source-state record holds loading, ready and failed. A typed `PlaybackFailed` event stops timeline playback and disables the playback and sync actions. The pane then hides the native surface and shows a fixed failure summary with collapsed diagnostics. A new load clears the failure.

## Playback And Timeline

`VideoPlaybackViewModel` holds play, sync, and timeline-playback state for one editor.
`VideoPlaybackContext` derives the session offset from `SessionTrackTimeline`'s
source-relative window origin and owns both playback-time conversions. Clip selection and
playback share that mapping; GPS alignment changes the map clock only.

- Sync: while sync is on and the video is paused, the video follows the telemetry cursor. The pane sets the playback context on every cursor move; only a changed context (another clip, timing, or alignment) seeks the player.
- Timeline playback: `VideoPlaybackViewModel` subscribes to the timeline's `PlaybackToggleRequested` (Space over a plot) and `PlaybackStopRequested` (primary click in a plot). While timeline playback is active, the video position drives the telemetry cursor. `IPlotTimeline.SetPlaybackActive(true)` stops the plots from moving the cursor on pointer input. When the driven cursor lands outside the visible range, the timeline pans that range and keeps its span.

Sync and timeline playback need a known telemetry duration.

## Alignment

Camera clocks drift, so a clip may need an offset. Alignment uses three built-in actions in the context menu of every recorded signal row:

1. "Mark video event here" records the clip shown at the clicked telemetry time and the clicked seconds.
2. "Mark telemetry event here" ends the flow and calls `ShiftAlignmentAsync(clipId, marked seconds - clicked seconds)`.
3. "Cancel video alignment" drops the pending mark.

The editor-scoped `TimelineAlignmentController` keeps one pending `TimelineAlignmentMark` (`GpsTrack` or `VideoClip`), so only one alignment flow (GPS or video) can be active at a time. Closing the editor discards the mark and drops late feedback. Accepted alignment writes complete in `VideoCoordinator`; its `StopAsync` drains them at application shutdown before services are disposed.

Trim and split change only session windows; video rows and their alignment stay fixed. Each window derives its visible clips and playback offset from the source timeline. This also applies to a split received from a peer. See [Trim and split](ui-workflows.md#trim-and-split).

## Session List Indicator

`RecordedSessionProjection` observes committed session, source and video inputs. Off the UI thread, it finds the latest added clip that overlaps each session window. Its summary carries that clip to `SessionListViewModel`, which only renders the row state:

- `HasPresentVideo` shows a video icon with the tooltip "video".
- `HasMissingVideo` shows the same icon dimmed with the tooltip "video missing".

## Platform Players

| Head | Factory | Backend |
| --- | --- | --- |
| Windows, Linux | `LibVlcVideoPlayerFactory` in `Sufni.App/Sufni.App.Video.LibVlc/` | LibVLCSharp; the Windows head bundles the native library through `VideoLAN.LibVLC.Windows`, and the Linux head loads the installed LibVLC |
| macOS | `MacOsVideoPlayerFactory` | `AppleAvPlaybackBackend` (AVFoundation) in `Sufni.App/AppleShared/` |
| iOS | `IosVideoPlayerFactory` | `AppleAvPlaybackBackend` |

Each head registers its factory through `PlatformRoot` with its other platform services. `AppComposition` validates that player registration agrees with `CanPlayVideo`.

`NativeVideoPlayer` owns source cancellation, the access lease and failure reporting
on every platform. `AppleVideoSurface` shares AVFoundation preparation and status
observation between macOS and iOS. `LibVlcVideoSurface` parses media before it attaches
the media and observes native playback errors. Superseded callbacks cannot change the new
source. The view owns player start, stop and disposal.

`IVideoFileAccess` lends access for presence checks and for the full player source
lifetime:

- Desktop uses local read handles (`LocalVideoFileAccess`).
- iOS registers `BookmarkedVideoFileAccess`. It saves the storage provider's opaque bookmark under a device-local `VideoBookmark:` secure-storage key derived from the original path. A later process restores the bookmark through `IFilesService` and uses its resolved path.
- Avalonia iOS keeps security-scoped access alive through an open storage-file stream. Each lease owns that stream and the restored file until disposal.
- Presence checks use short leases. Playback keeps its lease until replacement, failure or view detach.
- Startup bookmark restoration waits until the mobile top-level storage provider is ready.
