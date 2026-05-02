# Desktop vs Mobile

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file is a cross-cutting reference for where the desktop and mobile experiences diverge: project layout, view selection, the runtime `IsDesktop` flag, navigation shells, platform-only services and coordinators, DI composition, scenario-specific solutions, and behavioral differences worth knowing. Per-feature details live in the linked subsystem docs; this file does not re-derive them.

## Table of Contents

- [Project Layout](#project-layout)
- [View Selection](#view-selection)
- [The `IsDesktop` Flag](#the-isdesktop-flag)
- [Navigation Shells](#navigation-shells)
- [Desktop-Only Surface](#desktop-only-surface)
- [Mobile-Only Surface](#mobile-only-surface)
- [DI Composition](#di-composition)
- [Solution Scoping](#solution-scoping)
- [Testing](#testing)
- [Behavioral Differences](#behavioral-differences)

## Project Layout

The shared application code lives in `Sufni.App/Sufni.App/Sufni.App/` and is consumed by every platform head. Desktop-only infrastructure (sync server, ASP.NET Core hosting) is factored out into `Sufni.App/Sufni.App.Desktop/`, which the three desktop heads reference; mobile heads reference the shared project directly. The full table of projects and roles lives in [ARCHITECTURE.md § Project Structure](../ARCHITECTURE.md#project-structure).

```
                  Sufni.App (shared)
                  /              \
       Sufni.App.Desktop         (referenced directly)
       /     |       \           /              \
 Windows   macOS    Linux    Android           iOS
```

Only desktop heads pull in `Sufni.App.Desktop`, which is what keeps `Microsoft.AspNetCore.App` and the ECDSA / JWT machinery off the mobile builds.

## View Selection

The shared project carries two parallel view folders:

- `Views/` — the canonical view per view model, used on both shells unless overridden.
- `DesktopViews/` — extended layouts (side panels, multi-pane editors, richer item rows) that desktop builds substitute in place of the mobile view.

The `ViewLocator` (`Sufni.App/Sufni.App/Sufni.App/ViewLocator.cs`) holds two dictionaries keyed by view-model type — a shared `ViewFactories` and a desktop-only `DesktopViewFactories`. On desktop the locator checks the desktop dictionary first and falls back to the shared one; on mobile only the shared dictionary is consulted. View models that need a desktop-specific layout simply have an entry in both dictionaries (e.g. `BikeEditorViewModel` → `BikeEditorView` on mobile, `BikeEditorDesktopView` on desktop). Where the same view model has both, the two views share a small UserControl base (e.g. `MainPagesViewBase`) only when the code-behind is shared — there is no general "two-view base class" requirement.

`Views/Controls/` holds reusable controls that bind against shared view-model bases (`CommonButtonLine` against `TabPageViewModelBase`, etc.). `DesktopViews/Controls/` holds the desktop-only row controls (`DeletableListItemButton`, `PairedDeviceListItemButton`, `LiveDaqListItemButton`) that are wired up only inside desktop list views. See [UI § Controls Library](ui.md#controls-library).

## The `IsDesktop` Flag

`App.IsDesktop` (`Sufni.App/Sufni.App/Sufni.App/App.axaml.cs`) is the single runtime source of truth for which shell is active. It is set once in `OnFrameworkInitializationCompleted` from the Avalonia application lifetime: `IClassicDesktopStyleApplicationLifetime` → `true`, `ISingleViewApplicationLifetime` → `false`.

The flag is consulted in three places, deliberately kept narrow:

- `ViewLocator` — picks `DesktopViewFactories` first when the flag is set.
- A handful of view models that expose desktop-only affordances as bindable booleans (e.g. `WelcomeScreenViewModel.IsDesktop` gates the "Open logs folder" link, `BikeEditorViewModel.CanChangeRearSuspensionMode`).
- `SessionDetailViewModel` and `DialogService` — branch into different load and dialog presentation paths (window vs in-tree overlay).

Coordinators and services do **not** branch on `IsDesktop` for behavior they would otherwise own. Where workflow really differs the divergence is expressed as separate coordinator entry points (e.g. `SessionCoordinator.LoadDesktopDetailAsync` vs `LoadMobileDetailAsync`) or as a separate coordinator type (`IShellCoordinator` implementations). Tests use `TestApp.SetIsDesktop(...)` — see [Testing](#testing).

## Navigation Shells

Both shells satisfy the same `IShellCoordinator` interface but back it with very different shell view models:

| Aspect              | Desktop (`DesktopShellCoordinator`)                                          | Mobile (`MobileShellCoordinator`)                                |
| ------------------- | ---------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| Shell host          | `MainWindowViewModel` with `ObservableCollection<TabPageViewModelBase> Tabs` | `MainViewModel` with `Stack<ViewModelBase>` view history         |
| `Open(view)`        | Add the tab if missing, set `CurrentView`                                    | Push current view onto history, set `CurrentView`                |
| `OpenOrFocus<T>`    | Walk `Tabs.OfType<T>().FirstOrDefault(match)`, reuse if found                | Always create + push (mobile has no concept of focusing)         |
| `Close(view)`       | Remove the tab, push it onto a `tabHistory` for `RestoreCommand`             | Pop if the supplied view is current                              |
| `GoBack()`          | No-op                                                                        | Pop one frame                                                    |
| Initial view        | `WelcomeScreenViewModel` is added as the first tab                           | `MainPagesViewModel` is the root; no welcome screen              |
| Hardware back       | n/a                                                                          | `TopLevel.BackRequested` wired in `App` to `OpenPreviousView()`  |

The full coordinator surface and rules live in [UI § Navigation](ui.md#navigation). Page lifecycle implications — `Loaded` / `Unloaded` for browse leases and store subscriptions — apply identically on both shells; see [UI § Threading & Lifecycle](ui.md#threading--lifecycle).

## Desktop-Only Surface

Desktop builds add the following beyond the shared registrations. None of these types or interfaces are referenced from mobile-platform heads.

- **Sync server**: `ISynchronizationServerService` / `SynchronizationServerService` (ASP.NET Core / Kestrel, TLS, JWT, mDNS advertisement of `_sstsync._tcp`). See [Sync § Server](sync.md#server).
- **Pairing server coordinator**: `IPairingServerCoordinator` re-exposes server pairing events as plain .NET events for the desktop pairing UI and provides a `StartServerAsync()` passthrough. See [UI § Coordinators](ui.md#coordinators).
- **Inbound sync coordinator**: `IInboundSyncCoordinator` is a marker interface whose implementation subscribes to `SynchronizationDataArrived` and writes incoming bikes / setups into their stores. Sessions and paired devices have their own dedicated coordinators so each store keeps exactly one writer.
- **Desktop-shaped Live DAQ surfaces**: the live preview / live session feature itself is shared (see [Live DAQ Streaming](live-streaming.md)), but the desktop heads provide extended layouts via `DesktopViews/Editors/LiveDaqDetailDesktopView`, `LiveSessionDetailDesktopView`, and the desktop-only Device Management card on the diagnostics tab.
- **Welcome screen**: instantiated only by `MainWindowViewModel` as the first tab; mobile boots straight into `MainPagesViewModel`.
- **DAQ import via mass storage**: drive-mounted DAQ devices (`BOARDID` marker scanning) and storage-provider folder picking are both viable on desktop. See [Acquisition § Mass Storage](acquisition.md#mass-storage).

Desktop heads register these via `DesktopAppBootstrapper.RegisterDesktopSync(...)` (`Sufni.App/Sufni.App.Desktop/DesktopAppBootstrapper.cs`).

## Mobile-Only Surface

Mobile builds add:

- **Sync client**: `ISynchronizationClientService` / `SynchronizationClientService` and the `IPairingClientCoordinator` that owns the `DeviceId` / `DisplayName` / `ServerUrl` / `IsPaired` source of truth, mDNS browse lifecycle, and the request / confirm / unpair HTTP plumbing. See [Sync § Client](sync.md#client).
- **`PairingClientViewModel`**: the pairing UI; desktop equivalents are `PairingServerViewModel`.
- **`IHapticFeedback`**: invoked from `HapticFeedbackBehavior` (a XAML attached behavior). Desktop heads do not register this and the behavior simply no-ops because the service resolution returns null.
- **`IFriendlyNameProvider`**: queried by `PairingClientCoordinator` to seed a default `DisplayName` for the pairing record.
- **A second mDNS keyed-service registration** under the `"sync"` key for `_sstsync._tcp` browse (desktop browses only `"gosst"` for DAQ discovery; the desktop *advertises* sync, mobile *browses* it).

Mobile heads register these inline in `MainActivity.CustomizeAppBuilder` (Android) or `AppDelegate.CustomizeAppBuilder` (iOS) before calling `base.CustomizeAppBuilder(builder)`.

## DI Composition

The shared DI container is `App.ServiceCollection` (a static `IServiceCollection`). Each platform entry point appends its registrations to it before Avalonia hands control to `App.OnFrameworkInitializationCompleted`, which then runs the shared registrations and finally calls `BuildServiceProvider()`. The rules:

1. The **platform head's `Program.cs` / `MainActivity` / `AppDelegate`** runs first and registers platform abstractions (`ISecureStorage`, `IServiceDiscovery`, optionally `IHapticFeedback`, `IFriendlyNameProvider`) plus the platform's sync side. Desktop heads call into `DesktopAppBootstrapper.RegisterDesktopSync(...)`; mobile heads register the sync client and pairing client coordinator inline.
2. **`App.OnFrameworkInitializationCompleted`** then registers the shared graph (services, stores, queries, coordinators, view models), records `IsDesktop`, builds the provider, and eagerly resolves the coordinators that subscribe to events at construction time.

The shell registration is one of the few places `OnFrameworkInitializationCompleted` itself branches by lifetime: `IClassicDesktopStyleApplicationLifetime` adds `DesktopShellCoordinator`, `ISingleViewApplicationLifetime` adds `MobileShellCoordinator`. The eager resolution at the end of `OnFrameworkInitializationCompleted` also branches: mobile resolves `IPairingClientCoordinator`; desktop resolves `IPairingServerCoordinator` and `IInboundSyncCoordinator`. The full registration list — including stores, coordinators, and view models — is in [UI § Dependency Injection](ui.md#dependency-injection).

`MainPagesViewModel` is constructed via an explicit factory because two of its dependencies (`PairingClientViewModel`, `PairingServerViewModel`) are platform-specific optionals; the factory uses `sp.GetService<...>()` rather than `sp.GetRequiredService<...>()` for those.

## Solution Scoping

`Sufni.App.sln` is the full-matrix solution. For day-to-day work the per-platform solutions scope the projects so the IDE only loads what is relevant:

| Solution            | Projects scoped                                                                                  |
| ------------------- | ------------------------------------------------------------------------------------------------ |
| `Sufni.Desktop.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.Desktop`, the three desktop heads (`Windows`, `macOS`, `Linux`), `Sufni.App.Tests`, `Sufni.Telemetry.Tests` |
| `Sufni.Android.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.Android`                          |
| `Sufni.iOS.sln`     | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.iOS`                              |

The mobile solutions deliberately omit `Sufni.App.Desktop` and the test projects so cold load and incremental build stay fast on the smaller scope.

## Testing

The headless test app (`Sufni.App.Tests/Infrastructure/TestApp.cs`) is a subclass of `App` that skips both XAML loading and the DI bootstrap. Tests that need a specific shell branch call `TestApp.SetIsDesktop(true)` or `TestApp.SetIsDesktop(false)` from inside an `[AvaloniaFact]`, which forwards to `App.SetIsDesktopForTests(...)`. View tests also use `ViewTestHelpers` which accepts the same flag. As called out in [CLAUDE.md](../../CLAUDE.md), tests should cover both branches whenever behavior differs by shell.

## Behavioral Differences

The places where the same workflow takes a meaningfully different desktop vs mobile path are deliberately small:

- **Session detail load**. `SessionDetailViewModel` calls `SessionCoordinator.LoadDesktopDetailAsync(...)` on desktop and `LoadMobileDetailAsync(...)` on mobile. The mobile path consults a `session_cache` row first, fetches missing telemetry from the paired desktop server transparently if the local blob is absent, and projects a smaller presentation; the desktop path always loads the full telemetry blob locally. See [UI § Coordinators](ui.md#coordinators) for the coordinator entry points and [Persistence § Schema](persistence.md#schema) for the `session_cache` row.
- **Sync direction**. Desktop hosts the server, mobile drives the client. There is no peer-to-peer mode and no path that runs both on one device. See [Sync](sync.md).
- **Editor presentation**. Desktop opens editors as additional `TabPageViewModelBase` tabs that can coexist with the list page; mobile pushes the editor onto the back stack and pops it on save / cancel. The same `IShellCoordinator` calls drive both.
- **Dialogs**. `DialogService` shows tile-layer and live-DAQ-config dialogs as standalone Avalonia `Window`s on desktop and as in-tree overlays anchored on `MainView` on mobile. Close-confirmation dialogs use the desktop `Window` form on both shells when an owner window is set.
- **Bike linkage editing**. `BikeEditorViewModel.CanChangeRearSuspensionMode` is desktop-only — the linkage / leverage-ratio editing affordances are not exposed on mobile because the linkage editor canvas is built for a desktop pointer interaction model.
- **Welcome screen and logs link**. The welcome screen is desktop-only, and even there the `Open logs folder` link is bound to `IsDesktop` so it can disappear cleanly if the screen is ever surfaced elsewhere.

Anything else (entity editing, save / conflict semantics, sensor calibration, plot rendering) goes through the same coordinators, services, and stores on both shells.
