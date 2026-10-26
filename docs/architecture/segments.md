# Saved Segments

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the `Segments/` slice: saved segments of recorded sessions, their persistence and synchronization, and how trim and split move them.

## Table of Contents

- [Overview](#overview)
- [Persistence](#persistence)
- [Store And Coordinator](#store-and-coordinator)
- [Synchronization](#synchronization)
- [Trim And Split](#trim-and-split)

## Overview

A saved segment is a named time range on one recorded session, in seconds from the session's telemetry start. A segment is live only while its session is live; that is derived when segments are read, so no delete cascades to segments.

The slice lives in `Sufni.App/Sufni.App/Segments/`:

- `Models/` — `SessionSegment` (table row, `Synchronizable`).
- `Services/` — `ISessionSegmentRepository`.
- `Stores/` — `SessionSegmentStore` behind `ISessionSegmentStore` and `ISessionSegmentStoreWriter`.
- `Coordinators/` — `SegmentCoordinator` behind `ISegmentCoordinator`.

## Persistence

`session_segment` holds id, `session_id`, `name`, `start_seconds`, `end_seconds`, and the `Synchronizable` columns `updated`, `client_updated`, and `deleted`. `DatabaseMigrationRunner` creates it with the core tables and gives it the sync-delta indexes. `ISessionSegmentRepository.GetLiveAsync` returns the live segments of live sessions. Rename and delete change only live rows; delete is a soft delete.

Startup cleanup removes segment tombstones older than one day and segments whose session row no longer exists. Builds that ran the old segment extension have every `private_saved_session_segment` row, tombstones included, copied once into `session_segment`; the copy drops the source table and its extension schema-version row.

## Store And Coordinator

`SessionSegmentStore` holds the live segments of live sessions. `GetForSession` returns the segments of one session. `PublishSegmentsAsync` publishes written rows; a deleted row leaves the store. `AppDataRefresher` refreshes the store with the other core stores.

`SegmentCoordinator` owns create, rename, and delete. It is the only place of the name rule: a name is required, and it is unique among the live segments of the session. `ValidateName` exposes the rule to prompts, and `NextName` returns the first free "Segment N" name. Create also rejects a missing session and a range that is not finite or not ordered. Each workflow returns `SegmentChangeResult.Applied` or `Failed` with a message.

## Synchronization

Segments travel as the `segment` list of `SynchronizationData` (protocol v5) through the core paths: the client applies remote rows verbatim, and the hub merges them by timestamp and tombstone after sessions in the same transaction. The client refreshes the store after a sync; on the hub, `InboundSyncCoordinator` refreshes it when a push carries segments. See [sync.md](sync.md).

## Trim And Split

The trim and split transactions in `SessionPersistenceTransactionRunner` move the session's segments in the same transaction (see [Trim and split](ui-workflows.md#trim-and-split)):

- Trim from beginning and undo trim shift every live segment of the session by the change of the session origin, so a segment keeps its place in the recording. A segment is never deleted by a trim.
- Split moves the segments that start at or after the click to the new second part, shifted to its origin. The other segments stay with the first part.

`SessionEditingCoordinator` publishes the changed segments with the changed sessions.
