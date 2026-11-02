# DAQ Management Protocol

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the side-channel protocol the app uses to talk to a connected DAQ for everything that is not raw file transfer over mass storage and not live telemetry preview: directory listing, file download, marking SST files as uploaded, remote trash, time sync, and CONFIG read/replace. Wire-level companion to [Data Acquisition](acquisition.md#network-wifi-daq) and [Live DAQ Streaming](live-streaming.md).

The management protocol is a separate framed TCP request/response protocol from the [LIVE protocol](live-streaming.md#live-wire-protocol). LIVE and MGMT share the DAQ's single-client TCP port advertised over mDNS as `_sufni._tcp`, so only one live or management connection can be active against a given DAQ at a time. The app currently uses this MGMT v2 wire protocol only for DAQ announcements whose TXT `live_proto` is `2`; diagnostics management is disabled for v3 live snapshots because MGMT v3 bytes are not specified. The diagnostics tab derives Set Time / CONFIG availability from catalog capability and an idle transport reservation (see [Reserved v2-only management](live-streaming.md#design-decisions)); network listing, fallback IDENTIFY and import acquire that same per-identity owner and return a typed busy holder before attempting device I/O.

## Service Surface

`IDaqManagementService` (`Sufni.App/Sufni.App/Acquisition/Services/IDaqManagementService.cs`) is the public surface; `DaqManagementService` (`Sufni.App/Sufni.App/Acquisition/Services/Management/DaqManagementService.cs`) is the implementation registered as a singleton in `AppComposition`.

| Method                  | Purpose                                                                              |
| ----------------------- | ------------------------------------------------------------------------------------ |
| `ListDirectoryAsync`    | Enumerate the `Root` directory contents                           |
| `ReadConfigAsync`       | Download and parse CONFIG into a `DaqConfigDocument` |
| `MarkSstUploadedAsync`  | Move an SST record from the device's root listing into its uploaded set              |
| `TrashFileAsync`        | Move an SST record into the device's trash set                                       |
| `SetTimeAsync`          | Five-sample PING round-trip clock sync, then issue `SET_TIME` with the corrected UTC |
| `WriteConfigAsync`    | Stream a new CONFIG payload using `PUT_FILE_BEGIN` + `PUT_FILE_CHUNK` + `PUT_FILE_COMMIT` |
| `OpenSessionAsync`      | Open a long-lived `IDaqManagementSession` for batched per-record work                |

Each top-level method opens a fresh TCP connection, runs one operation, and disposes the connection. `OpenSessionAsync(...)` is the exception: it returns an `IDaqManagementSession` (`Sufni.App/Sufni.App/Acquisition/Services/Management/IDaqManagementSession.cs`) that keeps a single connection open across `GetFileAsync`, `MarkSstUploadedAsync`, and `TrashFileAsync` calls and disposes it on `DisposeAsync()`. List, CONFIG read/write and set-time remain one-shot. The top-level API fixes the root directory and CONFIG target internally; callers do not supply wire directory or file-class ids. `ImportSessionsCoordinator` opens the selected datastore's `OpenImportBatchAsync` scope. A network batch lazily opens its management session on the first file operation and retains it through downloads, trash and post-publication acknowledgements. The batch disposes the session when the coordinator finishes. A failed open belongs to that file; the next file can try opening the session again. The coordinator has no concrete network-file or management-service dependency.

`ServiceEndpoint` carries all resolved address candidates through management
operations and import sessions; the TCP client tries that set within the existing
connection timeout.

`DaqManagementService` requires a `TimeProvider` for UTC and monotonic round-trip measurements.

Connection, per-frame I/O, and the `PUT_FILE_COMMIT` reply each have independent timeouts supplied by `DaqManagementService` (5s / 5s / 15s). Connection and operation-validation failures surface as `DaqManagementException`; low-level malformed-frame parsing can still throw `FormatException` from `ManagementProtocolReader`. Protocol-level errors are returned as a typed `DaqManagementResult.Error` / `DaqConfigReadResult.Error` / `DaqListDirectoryResult.Error` (session file transfer uses `DaqGetFileResult.Error`) carrying a mapped `DaqManagementErrorCode` and a human-readable message.

## Wire Protocol

The protocol is little-endian, framed, request/response with a per-request `RequestId` that the device echoes. Wire types and helpers live in `Sufni.App/Sufni.App/Acquisition/Services/Management/`:

- `ManagementProtocolModels.cs` — response records, frame types, result codes, payload sizes, error mapping.
- `ManagementProtocolReader.cs` — request builders and response parser, with an `UnreadByteBuffer`-backed accumulator that handles partial TCP reads. Client-request decoding and its records live in the management test support for the fake DAQ server.
- `ManagementClient.cs` — connection lifecycle, per-operation send/receive loop, response validation.

### Frame Header

Every frame is a 16-byte header followed by a typed payload:

| Offset | Size | Field           | Notes                                                 |
| ------ | ---- | --------------- | ----------------------------------------------------- |
| 0      | 4    | Magic           | `0x544D474D` (`"MGMT"` little-endian)                 |
| 4      | 2    | Version         | `2` (current `ManagementProtocolConstants.Version`)   |
| 6      | 2    | Frame Type      | See table below                                       |
| 8      | 4    | Request ID      | Echoed by the device; mismatched ids fail the operation |
| 12     | 4    | Payload Length  | Capped at `MaxPayloadLength = 1 MiB`                  |

Request ids are monotonically allocated per `ManagementClient` instance, wrapping past `uint.MaxValue` to `1`.

### Frame Types

| Direction | Type                       | Code | Payload                                                                                  |
| --------- | -------------------------- | ---- | ---------------------------------------------------------------------------------------- |
| App → DAQ | `ListDirectoryRequest`     | `1`  | `u16 directoryId + u16 reserved`                                                         |
| App → DAQ | `GetFileRequest`           | `2`  | `u16 fileClass + u16 reserved + i32 recordId`                                            |
| App → DAQ | `TrashFileRequest`         | `3`  | `i32 recordId`                                                                           |
| App → DAQ | `PutFileBegin`             | `4`  | `u16 fileClass + u16 reserved + u64 fileSizeBytes`                                       |
| App → DAQ | `PutFileChunk`             | `5`  | Up to `MaxPutFileChunkPayloadSize = 512` bytes of file content                           |
| App → DAQ | `PutFileCommit`            | `6`  | (empty)                                                                                  |
| App → DAQ | `SetTimeRequest`           | `7`  | `u32 utcSeconds + u32 microseconds`                                                      |
| App → DAQ | `Ping`                     | `8`  | (empty)                                                                                  |
| App → DAQ | `MarkSstUploadedRequest`   | `9`  | `i32 recordId`                                                                           |
| DAQ → App | `ListDirectoryEntry`       | `16` | `u16 dirId + u16 fileClass + i32 recordId + u64 fileSize + i64 timestampUtcSeconds + u32 durationMs + u8 sstVersion + 12-byte ASCII name` |
| DAQ → App | `ListDirectoryDone`        | `17` | `u32 entryCount`                                                                         |
| DAQ → App | `FileBegin`                | `18` | `u16 fileClass + u16 reserved + i32 recordId + u64 fileSizeBytes + u32 maxChunkPayload + 12-byte ASCII name` |
| DAQ → App | `FileChunk`                | `19` | File bytes (size bounded by `FileBegin.maxChunkPayload`)                                 |
| DAQ → App | `FileEnd`                  | `20` | (empty)                                                                                  |
| DAQ → App | `ActionResult`             | `21` | `i32 resultCode`                                                                         |
| DAQ → App | `Error`                    | `22` | `i32 errorCode` (only valid before a multi-frame stream starts)                          |
| DAQ → App | `Pong`                     | `23` | (empty)                                                                                  |

`DaqDirectoryId`: `Root = 1`, `Uploaded = 2`, `Trash = 3`. `DaqFileClass`: `Config = 1`, `RootSst = 2`, `UploadedSst = 3`, `TrashSst = 4`. ASCII names are read up to the first `0x00` terminator.

### Result Codes

`ActionResult.resultCode` and `Error.errorCode` share the same numeric space, defined in `ManagementResultCode`:

| Code | Name                | Meaning                                          |
| ---- | ------------------- | ------------------------------------------------ |
| `0`  | `Ok`                | `ActionResult` only                              |
| `-1` | `InvalidRequest`    | Malformed or unsupported request                 |
| `-2` | `NotFound`          | Requested file or directory not found            |
| `-3` | `Busy`              | Device cannot service the request right now      |
| `-4` | `IoError`           | Device I/O error during processing               |
| `-5` | `ValidationError`   | CONFIG validation rejected the upload            |
| `-6` | `UnsupportedTarget` | Requested target not supported by the device     |
| `-7` | `InternalError`     | Device internal error                            |

`ManagementProtocolHelpers.ToUserMessage(...)` maps these codes to fixed user-facing strings carried on the typed result records. An unknown raw code raises `DaqManagementException` instead of being returned as a typed error.

### Operation Choreography

- **List directory** — request, then a stream of `ListDirectoryEntry` frames terminated by `ListDirectoryDone`. `ListDirectoryDone.entryCount` is validated against the entries received. An `Error` frame is only accepted before the first entry.
- **Get file** — request, then `FileBegin` + 1..N `FileChunk` + `FileEnd`. Chunks are streamed straight to the destination as they arrive; the client enforces that no chunk exceeds `FileBegin.MaxChunkPayload` and that the cumulative chunk bytes equal the declared `FileSizeBytes`.
- **Trash / Mark uploaded / Set time** — request, single `ActionResult` reply. `Ok` → `DaqManagementResult.Ok`; any negative code → `DaqManagementResult.Error`. An `Error` frame in response to these is treated as a protocol violation.
- **Replace config** — `PutFileBegin` (acknowledged with an `ActionResult`; on error the upload aborts before any chunks are sent), then chunks of at most 512 bytes, then `PutFileCommit`. The commit reply uses the longer `commitTimeout` (default 15s) to allow the device to fsync and validate. A `ValidationError` at commit means the device rejected the new CONFIG.
- **Set time** — five `Ping` round-trips, three middle RTT samples retained, average midpoint UTC computed, then a single `SetTimeRequest` with the corrected seconds + microseconds. `Ok` carries the average measured RTT for diagnostics.

The client validates, on every reply, that the request id matches the active in-flight request, that magic and version on the header are valid, and that payload length matches the expected size for the declared frame type.

## Callers

- **`NetworkTelemetryDataStore`** (`Sufni.App/Sufni.App/Acquisition/Models/NetworkTelemetryDataStore.cs`) calls `ListDirectoryAsync(endpoint)` from `GetFiles()`. Network source presence comes from catalog entries supporting management (currently v2). Listing holds `Management(ListFiles)` and the batch holds `Import(batchId)` through session disposal. The returned `DaqRootDirectoryRecord.Files` is partitioned into `DaqSstFileRecord` (mapped into importable `NetworkTelemetryFile`s), `DaqMalformedSstFileRecord` (mapped into non-importable `NetworkTelemetryFile`s carrying the malformed message), and `DaqConfigFileRecord` (skipped — the import flow ignores CONFIG entries).
- **`NetworkTelemetryFile`** (`Sufni.App/Sufni.App/Acquisition/Models/NetworkTelemetryFile.cs`) implements the [`ITelemetryFile`](acquisition.md#interfaces) hooks against the management protocol: `ReadSourceAsync(...)` streams the recording through `GetFileAsync(..., DaqFileClass.RootSst, recordId, ...)` into a `MemoryStream` and returns those bytes as a `TelemetryFileSource`; `OnImported()` calls `MarkSstUploadedAsync(recordId)` after a successful session save; `OnTrashed()` calls `TrashFileAsync(recordId)` for the deferred-delete branch of the import workflow. All three obtain the current batch's `IDaqManagementSession` through their datastore-provided operation context. `DaqGetFileResult.Error` and `DaqManagementResult.Error` outcomes are rethrown as `DaqManagementException` so failures bubble up to `ImportSessionsCoordinator` and surface as per-file failures rather than terminating the batch.
- **`LiveDaqCoordinator`** owns diagnostics management. It looks up the current catalog entry and its `SupportsManagement` capability, acquires a named management reservation from `ILiveDaqSharedStreamRegistry`, and calls `SetTimeAsync`, `ReadConfigAsync` or `WriteConfigAsync` on the background runner. It awaits service cleanup before releasing the reservation. Typed outcomes distinguish success, a busy holder, offline/unsupported devices, known device rejection and failure with diagnostics. It also owns CONFIG picking and filename validation. Async disposal cancels and drains retained calls. `LiveDaqDetailViewModel` forwards intent and displays those results; the existing CONFIG editor callback receives safe messages through that presentation boundary.

The mass-storage telemetry-file path (`MassStorageTelemetryFile`) does not use this service: import and trash on USB drives move files to `uploaded/` and `trash/` subdirectories on the local filesystem. See [Mass Storage](acquisition.md#mass-storage).

## CONFIG Document

CONFIG is the DAQ's persistent settings file, exchanged through the domain `ReadConfigAsync` / `WriteConfigAsync` methods. The service selects `DaqFileClass.Config`, record id `0` internally and parses downloads into `DaqConfigDocument` (`Sufni.App/Sufni.App/Acquisition/Services/Management/DaqConfigDocument.cs`):

- `Parse(bytes)` UTF-8-decodes the payload, splits on LF (with optional preceding CR), and parses `KEY=value` lines. Each parsed line is paired with a `DaqConfigFieldDefinition` if the key matches a known field or one of its aliases; other lines are kept verbatim. Effective values default to per-field defaults and are overwritten by parsed assignments.
- `BuildText(values)` preserves the original line order, replaces each known field's first occurrence with `KEY=value` from the supplied dictionary, drops subsequent duplicate occurrences, and appends any known fields missing from the original document. The result is `\n`-joined and re-encoded as UTF-8 by `BuildBytes(...)`.

The known field set is enumerated in `DaqConfigFields` (`DaqConfigFieldDefinition.cs`) — WiFi mode/SSID/PSK (STA + AP), NTP server, country, timezone, and the three sample rates (travel / IMU / GPS). Each definition carries a label, default value, optional max length, secret flag, and parse-time aliases (e.g. `SSID` → `STA_SSID`, `PSK` → `STA_PSK`).

`DaqConfigValidator.Validate(values)` produces a per-key error dictionary used by the editor before re-uploading: max-length and `=`-free values for length-constrained fields, valid `WIFI_MODE` (`STA` / `AP`), `COUNTRY` length, sample-rate parseability (1..65535), and conditional WiFi-mode requirements (STA needs SSID + PSK; AP needs SSID + PSK ≥ 8 chars). Validation runs in the editor view model, not in the service.

`SelectedDeviceConfigFile` (`SelectedDeviceConfigFile.cs`) is the small record `IFilesService` returns when the user picks a CONFIG file from disk for direct upload through the Replace Config flow, bypassing the in-app editor.
