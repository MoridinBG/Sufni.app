# Terrain profiles

A profile describes moving ten-metre windows of one recorded session. The telemetry library derives suspension figures, roughness, steepness and matching signals from processed telemetry and original GPS fixes. Roughness and steepness report their availability independently. The calculator and calibration table each have a version.

## Materialization

`TerrainProfileCoordinator` owns the device-local profile. At startup and after session or track publications, it evaluates pending sessions on the background runner and processes one recording at a time, newest first. An editor can prioritize a queued recording. A changed input supersedes the run. Shutdown cancels and drains planning and calculation.

The content token identifies the processed telemetry revision, selected full track and points revision, aligned recording window and track projection version. `TerrainProfileKey` adds both terrain versions. The exact reader and the SQLite write transaction use the same token rule as the status query. A computed row contains a summary and MessagePack profile. A terminal outcome contains only its key and reason.

## Availability

`SessionTerrainStatusQuery` derives the list and editor status from the session snapshot and track catalog. A session without processed telemetry is unavailable. A new key is pending. With matching inputs, the status exposes the current summary or the stored outcome. A recording with no selected track reaches `NoTrack` without a telemetry read. A missing exact payload yields `ContentMissing`. A stale read yields no write.

A calculation failure is recorded for its input key. If the database write itself fails, the coordinator publishes the failed status for the current app run. Reopening the database allows backfill to retry. Authored metadata and sync clocks are independent of profile materialization.

## Recorded editor

The editor reads the exact profile on its background runner and derives timed runs for both axes, segmented sensor and grade signals, and distance shares for jointly classified, frame-only and lowers-only windows. The reducer accepts a loaded result only for the current domain status. A change of status immediately removes the previous plot and map data.

The source label identifies both sensors when both contributed anywhere in the ride. The tooltip distinguishes separate contributions from joint classification and identifies clipping, missing samples or an uncalibrated scale. The grade trace uses the same five-window baseline as steepness classification.
