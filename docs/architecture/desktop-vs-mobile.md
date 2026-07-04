# Platform Capabilities And Layout Profiles

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file is a cross-cutting reference for the remaining platform divergence points after the unified shell refactor: project layout, layout-profile view selection, platform capabilities, native lifetime hosts, platform-only services, DI composition, scenario-specific solutions, and behavioral differences worth knowing. Per-feature details live in the linked subsystem docs; this file does not re-derive them.

## Table of Contents

- [Project Layout](#project-layout)
- [Layout Profiles And View Selection](#layout-profiles-and-view-selection)
- [Platform Flag And Capabilities](#platform-flag-and-capabilities)
- [Shared Navigation Shell](#shared-navigation-shell)
- [Desktop-Capable Surface](#desktop-capable-surface)
- [Mobile-Capable Surface](#mobile-capable-surface)
- [DI Composition](#di-composition)
- [Solution Scoping](#solution-scoping)
- [Testing](#testing)
- [Behavioral Differences](#behavioral-differences)

## Project Layout

The shared application code lives in `Sufni.App/Sufni.App/` and is consumed by every platform head. Desktop-only infrastructure (sync server, ASP.NET Core hosting) is factored out into `Sufni.App/Sufni.App.Desktop/`, which the three desktop heads reference; mobile heads reference the shared project directly. The full table of projects and roles lives in [ARCHITECTURE.md § Project Structure](../ARCHITECTURE.md#project-structure).

```
                  Sufni.App (shared)
                  /              \
       Sufni.App.Desktop         (referenced directly)
       /     |       \           /              \
 Windows   macOS    Linux    Android           iOS
```

Only desktop heads pull in `Sufni.App.Desktop`, which is what keeps ASP.NET Core server hosting, JWT bearer validation, and server certificate generation off the mobile builds. Shared client-side JWT parsing still lives in `Sufni.App` because mobile sync clients need to inspect access-token expiry for refresh.

## Layout Profiles And View Selection

The UI has two local layout profiles:

- `Compact` is the mobile-shaped presentation and is the default profile for mobile heads.
- `Workspace` is the desktop-shaped presentation and is the default profile for desktop heads.

The selected profile is a local preference read during startup through `IAppEnvironment.LayoutProfile`; it is not synced. Changing it saves the preference immediately and applies on the next app launch.

`ViewLocator` chooses from three registries keyed by view-model type:

- `CommonViewFactories` for views that do not vary by profile.
- `CompactViewFactories` for compact-profile views.
- `WorkspaceViewFactories` for workspace-profile views.

This means a mobile platform can render workspace-profile views when the local preference requests it, and a desktop platform can render compact-profile views. Platform capabilities still decide which actions are available inside those views.

The older folder names remain for continuity: `Views/` mostly contains common or compact-profile views, while `DesktopViews/` mostly contains workspace-profile views. The folder name is not the selection mechanism; `IAppEnvironment.LayoutProfile` is.

## Platform Flag And Capabilities

`App.IsDesktop` still exists as a native lifetime/platform fact. It is set once in `OnFrameworkInitializationCompleted` from the Avalonia application lifetime and is used at the composition edge for platform concerns such as extension service-registration context, platform-only eager coordinator resolution, and desktop window versus single-view lifetime wiring.

User-visible UI behavior should not branch on `App.IsDesktop`. It should use:

- `IAppEnvironment.LayoutProfile` for compact versus workspace presentation.
- `AppCapabilities` for platform-only app actions such as sync server hosting, pairing client support, mass-storage import, storage-provider import, haptics, and native windowing.
- `InputCapabilities` for pointer, touch, keyboard, pinch, and long-press context-menu behavior.

Tests configure these explicitly through service setup or test helpers rather than relying on platform inference.

## Shared Navigation Shell

Both platform lifetimes use `ShellRootViewModel`, `ShellWorkspaceViewModel`, and `ShellWorkspaceCoordinator`.

`ShellWorkspaceViewModel` owns the logical tab collection, selected tab, tab history, open/focus, background open, close, restore, back, and reorder behavior. `ShellWorkspaceCoordinator` is the single `IShellCoordinator` implementation; callers still use `Open`, `OpenOrFocus<T>`, `OpenInBackground<T>`, `Close`, `CloseIfOpen<T>`, and `GoBack()`.

The layout profile decides how that shared workspace is presented:

| Aspect | Compact profile | Workspace profile |
| --- | --- | --- |
| Shell root view | `CompactShellView` | `WorkspaceShellView` |
| Primary navigation | drawer/bottom-tabs style primary surface | persistent rail and tab strip |
| Detail surfaces | current primary page plus focused detail surface | open tabs in the workspace |
| Open/focus semantics | shared dedupe/focus/background/close semantics | same shared semantics |
| Back behavior | `GoBack()` returns to the previously focused tab or primary surface | same workspace history behavior |
| Initial view | primary navigation visible, no welcome tab | neutral empty workspace state until a tab opens |

Mobile native lifetime still creates `MainView` and `MobileNavigationShellHost` so Avalonia single-view integration, safe-area behavior, plot overlays, and hardware back wiring have a native host. That host mirrors the shared workspace; it is not a separate navigation model.

## Desktop-Capable Surface

Desktop heads register capabilities and services that mobile heads do not provide:

- **Sync server**: `ISynchronizationServerService` / `SynchronizationServerService` (ASP.NET Core / Kestrel, TLS, JWT, mDNS advertisement of `_sstsync._tcp`). See [Sync § Server](sync.md#server).
- **Pairing server coordinator**: `IPairingServerCoordinator` re-exposes server pairing events as plain .NET events for the pairing UI and provides a `StartServerAsync()` passthrough.
- **Inbound sync coordinator**: `IInboundSyncCoordinator` subscribes to `SynchronizationDataArrived` and writes incoming bikes / setups into their stores. Sessions and paired devices have their own dedicated coordinators so each store keeps exactly one writer.
- **Mass-storage DAQ import**: drive-mounted DAQ devices (`BOARDID` marker scanning) are available only when `AppCapabilities.SupportsMassStorageImport` is true. Storage-provider import is separately gated by `SupportsStorageProviderImport`.
- **Native windowing**: window ownership, dialog windows, and raw window shortcuts are available only when capabilities indicate native windowing support.

The workspace profile does not grant these capabilities by itself. For example, workspace profile on iOS must not expose the desktop sync server or mass-storage import.

## Mobile-Capable Surface

Mobile heads register capabilities and services that desktop heads generally do not provide:

- **Sync client**: `ISynchronizationClientService` / `SynchronizationClientService` and `IPairingClientCoordinator`, which owns the `DeviceId` / `DisplayName` / `ServerUrl` / `IsPaired` source of truth, mDNS browse lifecycle, and the request / confirm / unpair HTTP plumbing. See [Sync § Client](sync.md#client).
- **`PairingClientViewModel`**: the pairing client UI; it opens as a shared workspace tab/page surface.
- **`IHapticFeedback`**: invoked from `HapticFeedbackBehavior` when the platform registers a haptics implementation.
- **`IFriendlyNameProvider`**: queried by `PairingClientCoordinator` to seed a default display name for the pairing record.
- **Touch input capabilities**: pinch zoom and long-press context menus are exposed through `InputCapabilities`, not through the layout profile.

Mobile heads register these inline in `MainActivity.CustomizeAppBuilder` (Android) or `AppDelegate.CustomizeAppBuilder` (iOS) before calling `base.CustomizeAppBuilder(builder)`.

## DI Composition

The shared DI container is `App.ServiceCollection` (a static `IServiceCollection`). Each platform entry point appends its registrations to it before Avalonia hands control to `App.OnFrameworkInitializationCompleted`, which then runs the shared registrations and calls `BuildServiceProvider()`.

1. The platform head registers platform abstractions (`ISecureStorage`, `IServiceDiscovery`, optionally `IHapticFeedback`, `IFriendlyNameProvider`), platform sync services, and `IAppEnvironment` with the platform default layout profile and capabilities. Desktop heads call into `DesktopAppBootstrapper.RegisterDesktopSync(...)`; mobile heads register the sync client and pairing client coordinator inline.
2. `App.OnFrameworkInitializationCompleted` registers the shared graph: stores, services, queries, view models, `ShellWorkspaceViewModel`, `ShellRootViewModel`, `ShellWorkspaceCoordinator`, and the profile-aware `ViewLocator`.
3. Single-view lifetimes also register `MobileNavigationShellHost` / `IMobileNavigationShellHost` / `IMobileNavigationPageHost` for native host integration.
4. After the provider is built, the app eagerly resolves coordinators that subscribe to external events at construction time. The platform-only eager resolutions still follow platform capabilities: mobile resolves the pairing client coordinator; desktop resolves pairing server and inbound sync coordinators.

Extension module startup is part of the same composition root. `AppExtensionServiceRegistrationContext.IsDesktop` remains platform-based for service registration. Extension view registration and lookup are profile-based.

## Solution Scoping

`Sufni.App.sln` is the full-matrix solution. For day-to-day work the per-platform solutions scope the projects so the IDE only loads what is relevant:

| Solution | Projects scoped |
| --- | --- |
| `Sufni.Desktop.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.Desktop`, the three desktop heads (`Windows`, `macOS`, `Linux`), `Sufni.App.Tests`, `Sufni.Telemetry.Tests` |
| `Sufni.Android.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.Android` |
| `Sufni.iOS.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.iOS` |

The mobile solutions deliberately omit `Sufni.App.Desktop` and the test projects so cold load and incremental build stay fast on the smaller scope.

## Testing

The headless test app (`Sufni.App.Tests/TestSupport/Harness/TestApp.cs`) is a subclass of `App` that skips both XAML loading and the DI bootstrap. Tests configure layout profile and capabilities explicitly when those are the behavior under test. `TestApp.SetIsDesktop(...)` is reserved for the remaining platform-lifetime edges, such as extension service-registration context.

View-test helpers should select compact/workspace profile and capabilities explicitly rather than assuming platform-based view splits.

## Behavioral Differences

The places where platform behavior still differs are deliberately small:

- **Session detail load**. `SessionDetailViewModel` calls one local-only `SessionCoordinator.LoadDetailAsync(...)` path in both profiles. Missing processed telemetry or recorded-source payloads surface as an incomplete-local-data screen state; sync is responsible for downloading those payloads before the session is opened. See [UI Workflows § Coordinators](ui-workflows.md#coordinators) and [Sync](sync.md).
- **Sync side**. Desktop-capable platforms can host the server; mobile-capable platforms pair and sync as clients. There is no peer-to-peer mode and no path that runs both on one device unless a future platform registers both capabilities deliberately.
- **Platform-only actions**. Sync server actions, pairing client actions, haptics, mass-storage import, storage-provider import, and native windowing are all capability-gated.
- **Dialogs**. `DialogService` shows generic prompts as standalone Avalonia `Window`s when the native lifetime has an owner window, and as in-tree overlays when the lifetime is single-view.
- **Zoomed plot modal**. Double-clicking or double-tapping a plot opens the same live plot control in `PlotZoomOverlayHost`. Workspace profile shows a framed modal over a scrim; compact profile uses an edge-to-edge modal inside the safe area and rotates plot content in portrait-shaped windows. Escape close follows keyboard input capability, and single-view hosts participate in the hardware-back chain through `TryCloseTransientShellSurface`.
- **Bike linkage editing**. `BikeEditorViewModel.CanChangeRearSuspensionMode` remains desktop-capability-oriented because the linkage editor canvas is built for a pointer-heavy interaction model.

Anything else (entity editing, save / conflict semantics, sensor calibration, shell navigation semantics, session detail loading, and plot model rendering) goes through the same coordinators, services, stores, and workspace model on every platform.
