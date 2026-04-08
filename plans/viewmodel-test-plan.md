# ViewModel Test Plan

Status: proposed.

## Progress

- [ ] Prerequisite refactor 1 — pairing countdown made view-owned (investigate first)
- [ ] Prerequisite refactor 2 — `SetupListViewModel` → `ImportSessionsViewModel` coupling removed
- [ ] Prerequisite refactor 3 — `ItemListViewModelBase` delay duration parameterized
- [ ] Editor gap audit (`BikeEditorViewModelTests`, `SetupEditorViewModelTests`, `SessionDetailViewModelTests`)
- [ ] `BikeListViewModelTests`
- [ ] `SessionListViewModelTests`
- [ ] `SetupListViewModelTests`
- [ ] `PairedDeviceListViewModelTests`
- [ ] `ItemListViewModelBaseTests`
- [ ] `PairingClientViewModelTests`
- [ ] `PairingServerViewModelTests`
- [ ] `MainWindowViewModelTests`
- [ ] `MainViewModelTests`
- [ ] `WelcomeScreenViewModelTests`
- [ ] `BikeRowViewModelTests`
- [ ] `SessionRowViewModelTests`
- [ ] `SetupRowViewModelTests`
- [ ] `PairedDeviceRowViewModelTests`
- [ ] `LinkViewModelTests`
- [ ] `JointViewModelTests`
- [ ] `NotesPageViewModelTests`
- [ ] `RotationalShockSensorConfigurationViewModelTests`
- [ ] `RotationalForkSensorConfigurationViewModelTests`
- [ ] `LinearShockSensorConfigurationViewModelTests`
- [ ] `LinearForkSensorConfigurationViewModelTests`
- [ ] `SensorConfigurationViewModelTests` (factories)

Scope of this document: the next round of tests to add under `Sufni.App.Tests/ViewModels/`. Every concrete ViewModel that is not a pure observable property bag is covered, plus a gap audit on the three editor test files that already exist. `ImportSessionsViewModelTests` and `MainPagesViewModelTests` are deferred to follow-on phases — see "Deferred" below.

## Ground rules

Inherited from `additional-coordinator-test-plan.md`:

- xUnit + NSubstitute + `Avalonia.Headless` test style already established in `Sufni.App.Tests`.
- Test the view model contract and observable side effects, not implementation trivia.
- Do not assert on exception messages. Assert on result kinds, outcome types, and the *existence* of failure entries — never on the text a caller would surface to the user.
- Never instantiate real dependencies of the SUT. Every collaborator passed into a view model constructor must be an NSubstitute substitute or a dedicated test fixture. Test-only subclasses of abstract/base types used as *inputs* to the SUT (e.g., a minimal `TabPageViewModelBase` subclass passed into a shell tab list) are allowed — they are test data, not SUT dependencies.
- For view models that post work through `Dispatcher.UIThread`, use `AvaloniaFact` and explicitly drain queued UI work before asserting.
- Keep signal-processing assertions out of view model tests. Telemetry math belongs in `Sufni.Telemetry` tests.

View-model-specific additions:

- `[ObservableProperty]`-backed properties with no partial handler and no side effect are source-generator output and should not have dedicated tests. Cover them only through behavior that already flows through them (e.g. a command binding, a dirtiness check, a filter rebuild).
- Reactive pipelines that consume a store via `store.Connect()` are driven in tests by pushing `IChangeSet<TSnapshot, TKey>` values through an NSubstitute-returned `Subject<IChangeSet<...>>`. The test asserts on `Items` (the projected row collection) after each push.
- Event-reaction wiring (`coordinator.XxxChanged += ...`) is tested by raising the event through NSubstitute and asserting on the resulting VM state after draining `Dispatcher.UIThread` when required.
- **Targeted relaxation of the substitute-only rule for parameterless-stub VM fixtures.** `MainWindowViewModel` and `MainViewModel` each take a `MainPagesViewModel` (and the desktop one additionally takes a `WelcomeScreenViewModel`) in their constructor. These constructor parameters are stored for binding, not invoked as collaborators. The corresponding tests pass real instances constructed through the parameterless stub constructors of those VMs (which initialize service dependencies to `null!` and expose only the empty observable collections from the base classes). Do **not** extend this relaxation to VMs the SUT actually calls methods on — those must still be substituted.

## Prerequisite refactors

These production-code changes must land before the corresponding test files can be written. Investigate refactor 1 first — its scope depends on what the view file already does, which shapes the rest of the sequencing.

### Refactor 1 — make the pairing countdown view-owned

Blocks: `PairingServerViewModelTests`.

The countdown is currently rendered inside `Sufni.App/Sufni.App/DesktopViews/MainPagesDesktopView.axaml` (there is no standalone pairing view). Line 210 binds a `PathGeometry.Data` to `PairingServerViewModel.Remaining` via the `ProgressToArcConverter` declared inline in `MainPagesDesktopView.axaml.cs`. The pairing panel is gated on `IsVisible="{Binding !!PairingServerViewModel.PairingPin}"`.

Goal: move the countdown behavior out of the view model. The VM owns only semantic pairing state (`PairingPin`, `RequestingId`, `RequestingDisplayName`, `RequestingName`, and the `PairingConfirmed` → `PairingPin = null` reset). The per-second decrement and the "clear the PIN when the countdown expires" branch become the view's concern, animated from the moment `PairingPin` becomes non-null through `SynchronizationServerService.PinTtlSeconds` seconds.

Decide the exact animation mechanism after opening `MainPagesDesktopView.axaml`. Likely options, in rough order of preference:

- An Avalonia `Animation` / `Transition` that animates a view-local double from 1.0 → 0.0 over the PIN TTL, feeding the same `ProgressToArcConverter` the view already uses.
- A view-code-behind handler that starts / stops an animation timer on `DataContext.PairingPin` property-change notifications.

Do not prescribe the animation shape in this plan — pick it while looking at the file. The deliverable is: after the refactor, `PairingServerViewModel` has no `System.Timers.Timer`, no `Remaining` property, and the desktop view still draws the same countdown arc.

Tests then cover:

- `PairingRequested` populates `PairingPin` / `RequestingId` / `RequestingDisplayName`.
- `RequestingName` prefers `RequestingDisplayName` and falls back to `RequestingId` when the display name is null / whitespace.
- `PairingConfirmed` clears `PairingPin` but leaves `RequestingId` / `RequestingDisplayName` alone (lock down current behavior).
- `LoadedCommand` calls `coordinator.StartServerAsync()` unless in design mode (design-mode branch probably skipped).

### Refactor 2 — delete `SetupListViewModel` → `ImportSessionsViewModel` coupling

Blocks: `SetupListViewModelTests`.

`ARCHITECTURE.md:86` explicitly forbids VM-to-VM business dependencies; `ISetupCoordinator.OpenCreateForDetectedBoardAsync()` already exists for the exact same "pick the current board" use case in `WelcomeScreenViewModel.AddSetup`.

Apply:

- Remove the `ImportSessionsViewModel importSessionsPage` constructor parameter from `SetupListViewModel`.
- Remove the private field.
- `AddImplementation()` body becomes: `_ = setupCoordinator.OpenCreateForDetectedBoardAsync();`

DI registration does not need to change: `App.axaml.cs` registers `SetupListViewModel` via a plain `AddSingleton<SetupListViewModel>()` and lets the container resolve the constructor — removing a parameter is a clean signature change, no argument to drop.

Behavior delta: previously the list's "add" command pre-populated the board id from whatever datastore was currently selected in the import screen (hidden cross-tab state); after the refactor it re-probes the OS at click time. This matches the welcome flow and removes the architecture violation.

### Refactor 3 — parameterize `ItemListViewModelBase` pending-delete window

Blocks: `ItemListViewModelBaseTests`, and any list view model test that wants to cover the full undo-window expiry path.

Change the private `const int PendingDeleteWindowMs = 3000;` into a `TimeSpan` constructor parameter with a 3-second default:

```csharp
protected ItemListViewModelBase(TimeSpan? pendingDeleteWindow = null)
{
    pendingDeleteWindowDuration = pendingDeleteWindow ?? TimeSpan.FromSeconds(3);
    ...
}
```

`StartUndoWindow` passes that `TimeSpan` directly to `Task.Delay`. Concrete list VMs either add a matching constructor parameter (defaulting to `null` so production uses the 3-second default) or expose a test-only constructor overload.

Tests pass `TimeSpan.FromMilliseconds(20)` so the expiry branch of `StartUndoWindow` completes deterministically in well under a second.


## Deferred

### `ImportSessionsViewModelTests`

Skipped in this phase. When the file is added in a follow-on phase it will also need a production refactor so that `OpenDataStore()` no longer constructs concrete `StorageProviderTelemetryDataStore` / `MassStorageTelemetryDataStore` directly, and so that `ImportSessionsInternal` is `async Task` instead of `new Thread(...).Start()`.

Do not perform the `ImportSessionsViewModel` refactor as part of this phase — bundle it with the test work when that phase is scheduled.

### `MainPagesViewModelTests`

Not tested at this time. `MainPagesViewModel` is a thin composition root for the main-window layout: it stitches the list view models together, installs cross-page menu items, mirrors three booleans from `ISyncCoordinator`, and forwards a handful of commands. Unit-testing it requires either a real constructor-seam refactor or a targeted relaxation that lets tests construct concrete list VMs as fixtures — either approach is disproportionate to the amount of logic the file actually owns. The sync mirroring is already covered by `SyncCoordinatorTests`; the per-list notifications are already covered by each list view model's own tests; the command forwards are trivial. Revisit only if a regression shows up that specifically lives in the composition layer.

## Recommended order

Biggest and most complex first. Refactor 1 (pairing countdown) is investigated ahead of everything else because it touches a view file shared with the main pages layout and its scope is the least predictable part of the plan.

0. Investigate refactor 1 and land it.
1. `BikeListViewModelTests`
2. `SessionListViewModelTests`
3. `SetupListViewModelTests` (depends on refactor 2)
4. `PairedDeviceListViewModelTests`
5. `ItemListViewModelBaseTests` (depends on refactor 3)
6. `PairingClientViewModelTests`
7. `PairingServerViewModelTests` (depends on refactor 1)
8. `MainWindowViewModelTests`
9. `MainViewModelTests`
10. `WelcomeScreenViewModelTests`
11. `BikeRowViewModelTests`
12. `SessionRowViewModelTests`
13. `SetupRowViewModelTests`
14. `PairedDeviceRowViewModelTests`
15. `LinkViewModelTests`
16. `JointViewModelTests`
17. `NotesPageViewModelTests`
18. `RotationalShockSensorConfigurationViewModelTests`
19. `RotationalForkSensorConfigurationViewModelTests`
20. `LinearShockSensorConfigurationViewModelTests`
21. `LinearForkSensorConfigurationViewModelTests`
22. `SensorConfigurationViewModelTests` (abstract base factories)

Plus a gap audit on the three existing editor test files, executed independently of the order above.

## Editor gap audit

`BikeEditorViewModelTests.cs`, `SetupEditorViewModelTests.cs`, and `SessionDetailViewModelTests.cs` already exist and set the baseline pattern. Before extending, audit each for missing cases against the editor's current public surface. The audit itself is done during the implementation phase — the plan only records the categories to check.

Categories the audit should verify are covered for each editor:

- Construction from every relevant snapshot shape (e.g. fork-only vs fork+shock bike, setup with / without each sensor configuration type, session with / without processed data).
- Dirtiness flips on every observable property the editor mutates (spot-check: any new property added since the tests were written).
- `SaveAsync` happy path routes through the coordinator with the correct payload and updates `BaselineUpdated`.
- `SaveAsync` conflict path surfaces the conflict result kind without mutating local state.
- `SaveAsync` failed path surfaces the failed result kind without mutating local state.
- `ResetAsync` / reset path restores baseline fields and clears dirtiness.
- `DeleteAsync` happy path routes through the coordinator.
- `DeleteAsync` `InUse` / `Failed` outcomes surface as error messages on the VM (where applicable).
- Coordinator arrival events (e.g. a session's processed data becoming available while the editor is open) refresh the editor's view of the underlying entity.

Record each identified gap as a new `[AvaloniaFact]` in the existing file — do not create parallel test files.

## ViewModel test matrix

One subsection per test file. Within each, cases are organized by the public surface being exercised.

### `BikeListViewModelTests`

Test cases:

- Constructor subscribes to `bikeStore.Connect()` and projects snapshots into `Items` as `BikeRowViewModel` instances. Pushing an `Add` change set produces one row with matching `Id` / `Name`.
- Updating a snapshot via `Update` on the change set updates the existing row in place (same instance reference), not a new row.
- Removing a snapshot via the change set removes the row from `Items`.
- `SearchText` change triggers a filter rebuild: rows whose `Name` does not contain the search text (case-insensitive) are hidden.
- A pending delete hides the affected row from `Items` immediately (via the filter predicate) and records `PendingName` on the base class.
- `FinalizeBikeDeleteAsync` calls `bikeCoordinator.DeleteAsync(id)` and, on `InUse`, appends an error message to `ErrorMessages`.
- `FinalizeBikeDeleteAsync` on `Failed` appends an error message with the outcome's error.
- `AddCommand` (bound through the base's `AddImplementation` override) calls `bikeCoordinator.OpenCreateAsync()`.
- `RowSelectedCommand` with a non-null row calls `bikeCoordinator.OpenEditAsync(row.Id)`.
- `RowSelectedCommand` with null row is a no-op.

Notes:

- Use a `Subject<IChangeSet<BikeSnapshot, Guid>>` for `bikeStore.Connect()`.
- Construct change sets with DynamicData's `ChangeSet<TSnapshot, TKey>` + `Change<TSnapshot, TKey>` records.
- `dependencyQuery.Changes` is a reactive stream consumed by `BikeRowViewModel` on construction — stub it with `Observable.Never<Unit>()` to keep the row's subscription alive without driving it.

### `SessionListViewModelTests`

Mirror the `BikeListViewModelTests` structure, plus the session-specific bits:

- Constructor projects snapshots sorted descending by `Timestamp ?? DateTime.MinValue`.
- `SearchText` match covers both `Name` and `Description`.
- `DateFilterFrom` / `DateFilterTo` changes trigger a filter rebuild that excludes rows outside the range (timestamp interpreted as local time from unix seconds).
- Snapshots with `Timestamp == null` are never excluded by the date filter.
- Pending delete, finalize, undo, and row selection cases as in `BikeListViewModelTests`.

### `SetupListViewModelTests`

Prerequisite: refactor 3.

Test cases:

- Constructor projects snapshots into `Items` as `SetupRowViewModel` instances (same DynamicData harness as the bike/session list tests).
- `SearchText` filter excludes rows whose name does not match.
- Pending delete hides the row immediately; finalize routes through `setupCoordinator.DeleteAsync(id)`; a `Failed` outcome appends an error.
- `AddCommand` (via `AddImplementation`) calls `setupCoordinator.OpenCreateForDetectedBoardAsync()` — no other arguments, no dependency on a datastore selection.
- `RowSelectedCommand` with a non-null row calls `setupCoordinator.OpenEditAsync(row.Id)`.

### `PairedDeviceListViewModelTests`

Test cases:

- Constructor projects snapshots into `Items` as `PairedDeviceRowViewModel` instances.
- Updating a snapshot via the change set updates the existing row in place.
- Pending delete hides the row immediately and records `PendingName` (falling back to `DeviceId` when `DisplayName` is blank).
- Finalize calls `pairedDeviceCoordinator.UnpairAsync(deviceId)` on expiry.
- A `Failed` unpair surfaces the error on `ErrorMessages`.

### `ItemListViewModelBaseTests`

Prerequisite: refactor 4. Tests use a minimal concrete subclass `TestItemList : ItemListViewModelBase` that exposes the protected pending-delete helpers and overrides `RebuildFilter` / `OnPendingDeleteUndone` with counters.

Test cases:

- `SearchBoxIsFocused = true` sets `DateFilterVisible = true`.
- `ClearSearchTextCommand` clears `SearchText` and sets `DateFilterVisible = false`.
- `ClearDateFilterCommand("from")` clears `DateFilterFrom` only.
- `ClearDateFilterCommand("to")` clears `DateFilterTo` only.
- `ToggleDateFilterCommand` flips `DateFilterVisible`.
- `StartUndoWindow` sets `PendingName` / `IsUndoVisible` and schedules a finalize callback to run after the (20 ms test) delay.
- The scheduled finalize callback actually runs after the delay (lock down the happy-path expiry branch).
- `UndoDeleteCommand` cancels the pending delete, clears `PendingName` / `IsUndoVisible`, calls `OnPendingDeleteUndone`, and does not run the finalize callback.
- `FinalizeDeleteCommand` flushes immediately: finalize callback runs, state clears, no second callback fires if the delay would have elapsed afterwards.
- Calling `StartUndoWindow` twice in a row flushes the first (runs its finalize) before installing the second.

Notes:

- Use `TaskCompletionSource` as the finalize callback body so the test can await callback completion deterministically.
- `[AvaloniaFact]` needed because `StartUndoWindow` marshals the expiry onto `Dispatcher.UIThread`.

### `PairingClientViewModelTests`

Test cases:

- Constructor seeds `DisplayName` / `ServerUrl` / `IsPaired` from the substituted `IPairingClientCoordinator`.
- Constructor subscribes to `DisplayNameChanged`, `ServerUrlChanged`, `IsPairedChanged`. Raising each event on the substitute updates the corresponding VM property after draining `Dispatcher.UIThread`.
- `RequestPairingCommand` on `Sent` flips `IsRequestSent = true`.
- `RequestPairingCommand` on `Failed` pushes an error onto `ErrorMessages` and leaves `IsRequestSent = false`.
- `ConfirmPairingCommand` on `Paired` clears `Pin`, clears `IsRequestSent`, clears `ErrorMessages`.
- `ConfirmPairingCommand` on `Failed` pushes an error onto `ErrorMessages` and leaves `Pin` / `IsRequestSent` unchanged.
- `UnpairCommand` on `Unpaired` surfaces nothing (no notification, no error).
- `UnpairCommand` on `LocalOnly` pushes a notification (not an error).
- `UnpairCommand` on `Failed` pushes an error.
- `LoadedCommand` calls `coordinator.StartBrowsing()`.
- `UnloadedCommand` calls `coordinator.StopBrowsing()`.
- `OpenPreviousPageCommand` calls `shell.GoBack()`.

Notes:

- `[AvaloniaFact]` required because each subscription handler calls `Dispatcher.UIThread.InvokeAsync`.

### `PairingServerViewModelTests`

Prerequisite: refactor 2.

Test cases:

- `PairingRequested` populates `PairingPin`, `RequestingId`, `RequestingDisplayName`.
- `RequestingName` returns `RequestingDisplayName` when it is non-blank.
- `RequestingName` falls back to `RequestingId` when `RequestingDisplayName` is null, empty, or whitespace.
- `RequestingName` raises `PropertyChanged` when either `RequestingId` or `RequestingDisplayName` changes.
- `PairingConfirmed` clears `PairingPin` but leaves `RequestingId` / `RequestingDisplayName` unchanged.
- `LoadedCommand` calls `coordinator.StartServerAsync()` when `Design.IsDesignMode` is false.

### `MainWindowViewModelTests`

Test cases:

- Constructor seeds `Tabs` with the supplied `WelcomeScreenViewModel` and sets it as `CurrentView`.
- `OpenView` adds a tab that is not already in `Tabs` and makes it current.
- `OpenView` with a tab already in `Tabs` does not add a duplicate and still sets it current.
- `CloseTabPage` removes the tab from `Tabs` and switches `CurrentView` to `previousActiveTab` when the closing tab was current.
- `CloseTabPage` of a non-current tab leaves `CurrentView` alone.
- `CloseTabPage` of the last remaining tab sets `CurrentView = null`.
- `CloseTabPage` pushes the closed tab onto the internal history stack so it can be restored.
- `RestoreCommand` pops the history stack and re-opens the most recently closed tab.
- `RestoreCommand` is a no-op when the history stack is empty.
- `IMainWindowShellHost.Tabs` exposes the same enumerable as the public `Tabs`.

Notes:

- Use test-only `TabPageViewModelBase` subclasses (the `TestTabPageViewModel` already introduced in `DesktopShellCoordinatorTests`) as input data.
- Constructor parameters `MainPagesViewModel` / `WelcomeScreenViewModel` are passed via their parameterless `null!`-stub constructors — these are test fixtures, not collaborators the SUT calls into.

### `MainViewModelTests`

Test cases:

- Constructor sets `CurrentView` to the supplied `MainPagesViewModel`.
- `OpenView(view)` pushes the current view onto the history stack and sets `CurrentView = view`.
- `OpenPreviousView()` pops the history stack and restores the previous view.
- `OpenPreviousView()` on an empty history stack is a no-op (does not mutate `CurrentView`).
- `IMainViewShellHost.CurrentView` / `OpenView` / `OpenPreviousView` route through the public members correctly.

Notes:

- `MainPagesViewModel` passed via its parameterless `null!`-stub constructor.
- Use test-only `ViewModelBase` subclasses as the views being pushed.

### `WelcomeScreenViewModelTests`

Test cases:

- `AddBikeCommand` calls `bikeCoordinator.OpenCreateAsync()`.
- `AddSetupCommand` calls `setupCoordinator.OpenCreateForDetectedBoardAsync()`.
- `ImportSessionCommand` calls `importSessionsCoordinator.OpenAsync()`.

### `BikeRowViewModelTests`

Test cases:

- Constructor from a snapshot populates `Id` / `Name`.
- `Update` replaces `Id` / `Name` with the new snapshot's values.
- `OpenPageCommand` calls `bikeCoordinator.OpenEditAsync(Id)`.
- `UndoableDeleteCommand` invokes the `requestDelete` callback with `this`.
- `UndoableDelete` / `FakeDelete` `CanExecute` returns `false` when `dependencyQuery.IsBikeInUse(Id)` returns true.
- Raising `dependencyQuery.Changes` refreshes both delete commands' `CanExecute`.
- `Dispose` unsubscribes from `dependencyQuery.Changes` (verify by raising another tick after dispose and confirming the command state does not refresh).

Notes:

- `dependencyQuery.Changes` stubbed with a `Subject<Unit>` so the test can drive it.

### `SessionRowViewModelTests`

Test cases:

- Constructor populates `Id` / `Name` / `Timestamp` / `IsComplete`.
- `Update` refreshes the same fields.
- Snapshot with `null` timestamp produces `Timestamp == null`.
- Snapshot with a unix timestamp produces a local-time `DateTime`.
- `OpenPageCommand` calls `sessionCoordinator.OpenEditAsync(Id)`.
- `UndoableDeleteCommand` invokes `requestDelete` with `this`.

### `SetupRowViewModelTests`

Test cases:

- Constructor populates `Id` / `Name` / `BoardId`.
- `Update` refreshes the same fields.
- `OpenPageCommand` calls `setupCoordinator.OpenEditAsync(Id)`.
- `UndoableDeleteCommand` invokes `requestDelete` with `this`.

### `PairedDeviceRowViewModelTests`

Test cases:

- Constructor populates `DeviceId` / `DisplayName` / `Expires`.
- `Name` prefers `DisplayName` when non-blank.
- `Name` falls back to `DeviceId` when `DisplayName` is null, empty, or whitespace.
- `Update` refreshes `DeviceId` / `DisplayName` / `Expires` and raises `PropertyChanged` for `Name` / `Timestamp`.
- `UndoableDeleteCommand` invokes `requestDelete` with `this`.
- `OpenPageCommand` / `FakeDeleteCommand` are safe no-ops (stubs for the shared control template).

### `LinkViewModelTests`

`[AvaloniaFact]` required — `LinkViewModel` touches `Avalonia.Media.SolidColorBrush`.

Test cases:

- Constructor from two joints sets `A`, `B`, `StartPoint`, `EndPoint`, and a name derived from the joint names.
- Constructor with an explicit name uses that instead of the derived one.
- Mutating `A.X` / `A.Y` updates `StartPoint`.
- Mutating `B.X` / `B.Y` updates `EndPoint`.
- Replacing `A` with a new joint detaches the previous joint's `PropertyChanged` handler (verify by mutating the old joint's X and confirming `StartPoint` does not change).
- `UpdateLength` with a supplied `pixelsToMillimeter` calculates `Length` as the euclidean distance in millimetres.
- `IsSelected = true` sets `Brush` to `RoyalBlue`.
- `IsSelected = false` sets `Brush` to `CornflowerBlue`.
- `FromLink` round-trips a `Link` via a joint-name lookup table.
- `ToLink(imageHeight, pixelsToMillimeters)` produces a `Link` with joints converted via `JointViewModel.ToJoint`.

### `JointViewModelTests`

`[AvaloniaFact]` required.

Test cases:

- Constructor populates `Name`, `Type`, `X`, `Y`, `ShowFlyout`.
- Constructor sets `Brush` according to `TypeToBrushMapping[Type]`.
- Mutating `Type` updates `Brush` to match the new type.
- `PointTypes` static collection exposes only `Fixed` and `Floating`.
- (If immutability is exposed publicly) `Immutability` reflects the constructor-set value.
- `ToJoint(imageHeight, pixelsToMillimeters)` produces a `Joint` model with the expected coordinate conversion.

### `NotesPageViewModelTests`

Test cases:

- Constructor initializes `ForkSettings` and `ShockSettings` as empty.
- `IsDirty(session)` returns false when every field matches.
- `IsDirty(session)` returns true when `Description` differs.
- `IsDirty(session)` returns true when any fork setting differs from the matching session field (one test per field: spring rate, HSC, LSC, LSR, HSR).
- `IsDirty(session)` returns true when any shock setting differs from the matching session field (one test per field).
- `IsDirty(session)` treats null-on-both-sides as "equal" (covered by the double-null guard on each branch — one test per branch to lock it down).

### `RotationalShockSensorConfigurationViewModelTests`

`[AvaloniaFact]` required — depends on `IReadOnlyList<JointViewModel>`.

Test cases:

- Constructor from a `RotationalShockSensorConfiguration` populates the relevant observable properties and leaves `IsDirty = false`.
- `EvaluateDirtiness` flips `IsDirty = true` when any mutable field differs from the backing config.
- `CanSave` returns false when any required field is null.
- `CanSave` returns true when all required fields are set.
- `Save` rebuilds the backing config and clears `IsDirty`.
- `ToJson` produces a round-trip-compatible string via the shared `SensorConfiguration.SerializerOptions`.

### `RotationalForkSensorConfigurationViewModelTests`

Mirror the above without the joints dependency.

### `LinearShockSensorConfigurationViewModelTests`

Test cases:

- Constructor from a `LinearShockSensorConfiguration` populates `Length` / `Resolution` and leaves `IsDirty = false`.
- Mutating `Length` or `Resolution` flips `IsDirty = true`.
- `CanSave` returns false when either field is null.
- `CanSave` returns true when both are set.
- `Save` rebuilds the backing config and clears `IsDirty`.
- `ToJson` round-trips through `SensorConfiguration.FromJson`.

### `LinearForkSensorConfigurationViewModelTests`

Mirror `LinearShockSensorConfigurationViewModelTests`.

### `SensorConfigurationViewModelTests`

Tests on the abstract base's static factories.

Test cases:

- `Create(null)` returns null.
- `Create(SensorType.LinearFork)` returns `LinearForkSensorConfigurationViewModel`.
- `Create(SensorType.LinearShock)` returns `LinearShockSensorConfigurationViewModel`.
- `Create(SensorType.RotationalFork)` returns `RotationalForkSensorConfigurationViewModel`.
- `Create(SensorType.RotationalShock, joints)` returns `RotationalShockSensorConfigurationViewModel` wired to the supplied joints.
- `FromJson(null)` returns null.
- `FromJson(linearForkJson)` returns the matching concrete VM populated from the JSON.
- `FromJson(linearShockJson)` returns the matching concrete VM.
- `FromJson(rotationalForkJson)` returns the matching concrete VM.
- `FromJson(rotationalShockJson, joints)` returns the matching concrete VM wired to the supplied joints.

## Small helper work likely needed

- A `TestChangeSet` helper that builds `IChangeSet<TSnapshot, TKey>` values from `Add` / `Update` / `Remove` tuples. Used by every list view model test.
- A `TestItemListPage` fixture implementing `IItemListPage` with real `ObservableCollection<>` fields (used by `MainPagesViewModelTests`).
- A minimal `TestItemList : ItemListViewModelBase` subclass exposing protected pending-delete helpers as public methods (used by `ItemListViewModelBaseTests`).
- The shell-coordinator test subclasses `TestTabPageViewModel` / `TestViewModel` are already available in the coordinator test files — they can be promoted into `Sufni.App.Tests/Infrastructure/` and reused by the shell view model tests.
- The production-code prerequisites (refactors 1–4) listed above.

## Exit criteria for this phase

- Add one ViewModel test file for each entry in the recommended order (minus the deferred `ImportSessionsViewModelTests`).
- Complete the editor gap audit and add any missing cases to the three existing editor test files in place.
- Cover at least the happy path and the key failure / edge path for every view model in scope.
- Keep all four prerequisite refactors minimal — no drive-by cleanup in the production code touched by the refactors.
- Do not broaden this phase into XAML view tests, end-to-end scenario tests, or the deferred `ImportSessionsViewModel` file.

## Follow-on phases

- `ImportSessionsViewModelTests`, bundled with the factory / async refactor described under "Deferred".
- Deeper integration tests that wire real stores into list view models (currently the coordinator tests already cover the store-writer side; list VM tests cover the read side; a full end-to-end test is a separate phase).
- Row view model tests that assert on the shared control template bindings are out of scope here — they belong to a headless-rendering phase, not a VM-logic phase.
