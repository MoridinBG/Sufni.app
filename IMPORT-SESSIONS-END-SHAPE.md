# Import Sessions End Shape Plan

## Purpose

- Make `ImportSessionsViewModel` straightforward to unit test.
- Keep file listing, file processing, and device/network I/O off the UI thread.
- Keep the import screen responsive while browsing and importing.
- Preserve the stable architectural boundaries even if `ARCHITECTURE.md` wording changes.

## Stable Boundaries

These are the boundaries worth preserving even if the document structure or naming evolves:

- View model: owns only screen state, command flow, and binding-friendly projection.
- Store: owns shared read state for an entity family and direct lookups over that state.
- Query: answers a business question; it does not own the shared entity collection.
- Coordinator: owns workflows with side effects and store writes.
- Service or factory: owns infrastructure-facing work such as picker integration, datastore creation, and background execution.

## End Shape

### `ImportSessionsViewModel`

The view model should keep only screen-scoped state:

- `TelemetryDataStores`
- `TelemetryFiles`
- `SelectedDataStore`
- `SelectedSetup`
- `NewDataStoresAvailable`
- `Notifications` and `ErrorMessages`

The view model should do the following:

- Start and stop browse in `Loaded` and `Unloaded`.
- React to selected datastore changes.
- Resolve `SelectedSetup` from `ISetupStore.FindByBoardId`.
- Request file loading asynchronously.
- Start import through `IImportSessionsCoordinator`.
- Refresh the current file list after import completes.

The view model should not do the following:

- No direct `IDatabaseService` access.
- No direct `Dispatcher.UIThread` calls in normal flow.
- No raw `Thread` creation.
- No direct construction of `StorageProviderTelemetryDataStore`.
- No blocking filesystem or network work.

Commands that perform async work should be `async Task` methods. Purely synchronous commands such as `Loaded`, `Unloaded`, and `ClearNewDataStoresAvailable` should remain synchronous.

Use `ImportSessionsCommand.IsRunning` as the end-shape source of truth for the screen busy state rather than a separate `ImportInProgress` property.

### Setup Resolution: Store

`SelectedSetup` should be resolved directly from `ISetupStore`.

Reason:

- `FindByBoardId` is a direct lookup over the owned setup read model.
- It does not cross multiple domains.
- It is not a business rule by itself.

This means `ImportSessionsViewModel` should stop using `IDatabaseService` for board-to-setup resolution and use `ISetupStore.FindByBoardId(Guid)` instead.

### Datastore Browse and Creation

`ITelemetryDataStoreService` should continue to own:

- The live `DataStores` collection.
- Mass-storage and network browse lifecycle.
- Add and remove behavior for discovered stores.

The service, or a small factory used by it, should also own creation of storage-provider datastores.

The target shape is one of these:

- Add a method such as `TryAddStorageProviderAsync(IStorageFolder folder)` on `ITelemetryDataStoreService`.
- Or add a dedicated `ITelemetryDataStoreFactory` and keep registration in the service.

The important rule is the same in either shape:

- The view model may ask the user for a folder through `IFilesService`.
- The view model should not construct a concrete datastore implementation itself.
- Duplicate detection for already-opened folders should live with datastore creation logic, not in the view model.
- The datastore-creation surface should return the concrete datastore instance that was created or chosen so the VM can explicitly assign `SelectedDataStore = returnedStore`.

Do not rely on the existing `TelemetryDataStores.CollectionChanged` add-path auto-selection for this. That handler only selects `TelemetryDataStores[0]` when `SelectedDataStore` is null; it does not select the newly-added store.

## Threading Model

### UI Thread

The UI thread should be used only for:

- Bound property updates.
- `ObservableCollection` mutation.
- Notifications and error messages.
- Native picker interaction through `IFilesService`.
- Lightweight store lookups such as `FindByBoardId`.

### Background Work

The following work should always run off the UI thread:

- Mass-storage scan work that touches the filesystem.
- Construction and initialization paths that touch the filesystem, including `MassStorageTelemetryDataStore` setup work.
- File enumeration from a datastore.
- Storage-provider initialization that touches the filesystem.
- SST parsing and PSST generation.
- `OnImported` and `OnTrashed` hooks.
- Slow filesystem and network work in datastore implementations.

This is required even when a method already returns `Task`.

Reason:

- `TelemetryDataStoreService` currently performs mass-storage detection on a `DispatcherTimer` tick and constructs `MassStorageTelemetryDataStore` on the UI thread.
- `MassStorageTelemetryDataStore` currently performs synchronous file and directory I/O in its constructor.
- `MassStorageTelemetryDataStore.GetFiles()` currently performs synchronous filesystem enumeration and wraps the result in `Task.FromResult`.
- Some other import steps are async by signature but still represent slow device, filesystem, or CPU work.

So the end shape needs an explicit background boundary, not only more `await`.

### `TelemetryDataStoreService`

`TelemetryDataStoreService` currently has a hard Avalonia threading dependency through `DispatcherTimer` and `Dispatcher.UIThread.Post`.

The end shape should narrow that dependency:

- Mass-storage scan and datastore construction should happen off the UI thread.
- Only `DataStores` collection mutation needs to be marshaled back to the UI thread.
- Network add and remove handling should follow the same rule.

The first pass does not need to fully remove Avalonia from the service, but it should remove slow filesystem work from the service's UI-thread paths.

### Background Runner Abstraction

Introduce a small abstraction for off-thread work, for example:

- `IBackgroundTaskRunner`

Its job is simple:

- Production: run blocking work on a background thread.
- Tests: run inline or through a deterministic fake.

This keeps thread hopping out of the view model and makes the coordinator and supporting services testable.

Candidate shape:

```csharp
public interface IBackgroundTaskRunner
{
    Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default);
    Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default);
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);
}
```

The exact signature can change. The important part is that the off-thread boundary is explicit and injectable.

### UI Marshaling

First-pass preferred shape:

- Let VM command methods run on the UI context.
- Create `Progress<SessionImportEvent>` inside the VM on the UI thread.
- Let the coordinator report progress from background work.
- Update collections and bound properties in the progress callback and after awaited operations complete.

That keeps the common path simple and removes most direct dispatcher usage from the VM.

For tests, do not add a dedicated dispatcher or progress abstraction in the first pass. VM tests that assert progress-driven updates should run with an installed test `SynchronizationContext` so `Progress<T>` callbacks are deterministic.

## Coordinator Responsibilities

`IImportSessionsCoordinator` should remain the owner of the import workflow:

- Load setup and bike.
- Build `BikeData`.
- Iterate files.
- Import, trash, or skip according to the existing semantics.
- Persist sessions.
- Upsert `SessionStore`.
- Report per-file progress and failures.

The coordinator should also own the off-thread execution of the heavy per-file workflow, using the background runner.

That background hop should wrap the entire `ImportAsync` workflow, including setup and bike loading, not only the per-file loop. The coordinator currently builds `BikeData` from database reads before iterating files, and those awaits should not resume back onto the caller's UI context.

The coordinator should not:

- Know about Avalonia dispatcher APIs.
- Mutate UI collections.
- Depend on view models.

## File Loading Flow

### Selected Datastore Changed

Target flow:

1. If the new selected datastore is `null`, clear `TelemetryFiles`, clear `SelectedSetup`, and return.
2. If the new selected datastore is non-null, clear `NewDataStoresAvailable`.
3. Resolve `SelectedSetup` from `ISetupStore`.
4. Start an async file-list load for the selected datastore.
5. Run the actual datastore `GetFiles()` work off thread.
6. When the load completes, update `TelemetryFiles` on the UI thread.
7. Ignore stale results if the user changed the selected datastore while the load was still running.

This requires a small stale-result guard.

Good options:

- Keep track of origin store and drop results that are not from it.
- Or keep a monotonically increasing load version and ignore any completion that is not the latest.

## Open Datastore Flow

Target flow:

1. VM asks `IFilesService` for a folder.
2. VM hands the folder to datastore creation logic owned by the service or factory.
3. Creation and initialization run off thread if they touch the filesystem.
4. Duplicate detection happens there.
5. The creation surface returns the concrete datastore instance that was created or selected.
6. The VM explicitly sets `SelectedDataStore = returnedStore`.

This keeps native UI interaction in the VM boundary while moving infrastructure creation out of it.

## Import Flow

Target flow:

1. `ImportSessionsCommand` is `async Task`.
2. VM checks that `SelectedDataStore` and `SelectedSetup` are present.
3. The command's generated `IsRunning` state becomes the busy source of truth.
4. While the command is running, the UI disables file-selection editing so the live `ITelemetryFile` instances are treated as frozen for the import duration.
5. VM creates progress reporting on the UI thread.
6. Coordinator performs the entire import workflow off thread and reports per-file results.
7. VM refreshes the datastore's file list off thread after import completes.
8. The post-import refresh uses the same stale-result guard as selection-change loading so a later datastore selection cannot be overwritten by an older refresh completion.
9. VM updates `TelemetryFiles` only if the refresh result is still current.

The UI must remain interactive during the import.

Expected behavior during import:

- No UI freeze.
- Buttons that should not re-enter import are disabled.
- Progress notifications continue to appear.
- The page remains responsive to redraw and general interaction.

## Testing Shape

### View Model Tests

The VM should become easy to test without Avalonia dispatcher assumptions or raw thread coordination.

Core tests:

- Selecting a datastore resolves `SelectedSetup` from `ISetupStore`.
- Selecting `null` clears `TelemetryFiles` and `SelectedSetup` without clearing `NewDataStoresAvailable`.
- Selecting a datastore loads files and populates `TelemetryFiles`.
- A stale file-load result is ignored after a later selection change.
- A stale post-import refresh result is ignored after a later selection change.
- Adding and removing datastores updates `SelectedDataStore` and `NewDataStoresAvailable` correctly.
- Opening a duplicate datastore path reports a notification.
- Opening a datastore through the service or factory explicitly selects the returned datastore instance.
- Import uses command `IsRunning`, forwards progress into notifications and errors, disables file-selection editing while running, and refreshes the file list.
- `Loaded` starts browse and subscribes to setup-store changes.
- `Unloaded` clears state, stops browse, and disposes subscriptions.
- VM tests that assert `Progress<T>` driven updates install a test `SynchronizationContext`.

### Coordinator Tests

The coordinator tests should continue to own the workflow assertions:

- Per-file import success and failure behavior.
- Session persistence.
- Store upserts.
- Trash semantics.
- Progress reporting.
- Background runner usage for the entire import workflow.

## Staged Refactor

1. Remove `IDatabaseService` from `ImportSessionsViewModel`, delete the orphaned `EvaluateSetupExists` method, and resolve setup through `ISetupStore` only.
2. Convert `GetDataStoreFiles` and `ImportSessionsInternal` from `async void` plus raw `Thread` usage to `async Task` helpers, and convert the `ImportSessionsCommand` body itself to `async Task` so the generated command exposes `IsRunning`.
3. Switch the import screen busy-state plan to command `IsRunning` and disable file-selection editing while import is running.
4. Add stale-result protection for both selection-change loads and post-import refresh.
5. Introduce the explicit background runner abstraction.
6. Route datastore file loading and the entire import coordinator workflow through that runner.
7. Move storage-provider datastore construction out of the VM and into the service or a small factory, with explicit returned-store selection in the VM.
8. Move mass-storage scan and constructor-triggered filesystem work out of the service's UI-thread paths.
9. Add focused VM tests for the import screen, including a test `SynchronizationContext` for progress assertions.
10. Clean up any leftover dispatcher calls that are only compensating for removed raw-thread logic.

## Decision Notes

- The architecture document may keep evolving; this plan treats the boundary rules as the invariant, not the current wording.
- The main correctness rule is simple: screen state stays in the VM, shared entity state stays in stores, business answers go to queries, and side-effecting workflows stay in coordinators and services.
- The main responsiveness rule is also simple: any work that touches slow filesystem, network, or processing paths must cross an explicit background boundary before it runs.
- `TelemetryDataStores` nullability is likely a leftover from the previewer-friendly parameterless-constructor pattern; removing the `?` is a small cleanup opportunity, not a design dependency for the refactor.
