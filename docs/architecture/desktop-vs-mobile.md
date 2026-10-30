# Platform Capabilities And Layout Profiles

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file is a cross-cutting reference for the remaining platform divergence points after the unified shell refactor: project layout, layout-profile view selection, platform capabilities, native lifetime hosts, platform-only services, DI composition, scenario-specific solutions, and behavioral differences worth knowing. Per-feature details live in the linked subsystem docs; this file does not re-derive them.

## Table of Contents

- [Project Layout](#project-layout)
- [Layout Profiles And View Selection](#layout-profiles-and-view-selection)
- [Platform Capabilities](#platform-capabilities)
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

Video players are head-specific. The Windows and Linux heads reference `Sufni.App/Sufni.App.Video.LibVlc/`. The macOS and iOS heads link `Sufni.App/AppleShared/AppleAvPlaybackBackend.cs` and each define an AVFoundation player factory. Android has no video player. See [video.md](video.md#platform-players).

## Layout Profiles And View Selection

The UI has two local layout profiles:

- `Compact` is the mobile-shaped presentation and is the default profile for mobile heads.
- `Workspace` is the desktop-shaped presentation and is the default profile for desktop heads.

The selected profile is a local preference read during startup through `IAppEnvironment.LayoutProfile`; it is not synced. Changing it saves the preference immediately and applies on the next app launch.

`ViewLocator` chooses from three registries keyed by view-model type:

- `CommonViewFactories` for views that do not vary by profile.
- `CompactViewFactories` for compact-profile views.
- `WorkspaceViewFactories` for workspace-profile views.

Shell roots supply `ShellRootPresentation`, which carries the root view model and
selected profile; the locator sets that root as the shell view's data context.

This means a mobile platform can render workspace-profile views when the local preference requests it, and a desktop platform can render compact-profile views. Platform capabilities still decide which actions are available inside those views.

The older folder names remain for continuity: `Views/` mostly contains common or compact-profile views, while `DesktopViews/` mostly contains workspace-profile views. The folder name is not the selection mechanism; `IAppEnvironment.LayoutProfile` is.

## Platform Capabilities

Platform heads establish the layout profile, application capabilities, and input capabilities at composition time. Shared behavior uses:

- `IAppEnvironment.LayoutProfile` for compact versus workspace presentation.
- `AppCapabilities` for platform-only app actions such as sync server hosting, pairing client support, mass-storage import, storage-provider import, video playback (`CanPlayVideo`, false on Android). Haptics follow service registration, and native windowing follows the Avalonia lifetime.
- `InputCapabilities` for pointer, touch, keyboard, long-press context menus and the command-key modifier; touch capabilities also drive pinch gestures. Each head supplies Control or Meta explicitly.

Tests configure these explicitly through service setup or test helpers rather than relying on platform inference.

## Shared Navigation Shell

Both platform lifetimes use `ShellRootViewModel`, `ShellWorkspaceViewModel`, and `ShellWorkspaceCoordinator`.

`ShellWorkspaceViewModel` owns the logical tab collection, selected tab, tab history, open/focus, background open, close, restore, back, and reorder behavior. `ShellWorkspaceCoordinator` is the single `IShellCoordinator` implementation; features use `OpenEditor(EditorKey, background)` and `CloseEditorAsync(EditorKey)`. The shell owns the editor factory, restoration and post-save `CompleteEdit`; non-editor surfaces retain `Open`, `Close` and `GoBack()`.

The layout profile decides how that shared workspace is presented:

| Aspect | Compact profile | Workspace profile |
| --- | --- | --- |
| Shell root view | `CompactShellView` | `WorkspaceShellView` |
| Primary navigation | drawer/bottom-tabs style primary surface | persistent rail and tab strip |
| Detail surfaces | current primary page plus focused detail surface | open tabs in the workspace |
| Open/focus semantics | shared dedupe/focus/background/close semantics | same shared semantics |
| Back behavior | `GoBack()` returns to the previously focused tab or primary surface | same workspace history behavior |
| Initial view | primary navigation visible, no welcome tab | neutral empty workspace state until a tab opens |

Mobile native lifetime creates `MainView` so Avalonia single-view integration, safe-area behavior, plot overlays, and hardware back wiring have a native host. That host mirrors the shared workspace; it is not a separate navigation model.

## Desktop-Capable Surface

Desktop heads register capabilities and services that mobile heads do not provide:

- **Sync server**: `ISynchronizationServerService` / `SynchronizationServerService` (ASP.NET Core / Kestrel, TLS, JWT, mDNS advertisement of `_sstsync._tcp`). See [Sync § Server](sync.md#server).
- **Pairing server coordinator**: `IPairingServerCoordinator` re-exposes server pairing events as plain .NET events for the pairing UI and provides a `StartServerAsync()` passthrough.
- **Inbound sync coordinator**: `IInboundSyncCoordinator` subscribes to `SynchronizationDataArrived` and publishes incoming bike/setup ids through store writer publish-only paths after the server merge has persisted them. Sessions and paired devices have their own dedicated coordinators so each store keeps exactly one writer.
- **Mass-storage DAQ import**: drive-mounted DAQ devices (`BOARDID` marker scanning) are available only when `AppCapabilities.SupportsMassStorageImport` is true. Storage-provider import is separately gated by `SupportsStorageProviderImport`.
- **Native windowing**: the Avalonia desktop lifetime supplies the owner window and window-hosted dialogs and shortcuts.

The workspace profile does not grant these capabilities by itself. For example, workspace profile on iOS must not expose the desktop sync server or mass-storage import.

## Mobile-Capable Surface

Mobile heads register capabilities and services that desktop heads generally do not provide:

- **Sync client**: `ISynchronizationClientService` / `SynchronizationClientService` and `IPairingClientCoordinator`, which owns the `DeviceId` / `DisplayName` / `ServerUrl` / `IsPaired` source of truth, mDNS browse lifecycle, and the request / confirm / unpair HTTP plumbing. See [Sync § Client](sync.md#client).
- **`PairingClientViewModel`**: the pairing client UI; it opens as a shared workspace tab/page surface.
- **`IHapticFeedback`**: invoked from `HapticFeedbackBehavior` when the platform registers a haptics implementation.
- **`IFriendlyNameProvider`**: queried by `PairingClientCoordinator` to seed a default display name for the pairing record.
- **Touch input capabilities**: pinch zoom and long-press context menus are exposed through `InputCapabilities`, not through the layout profile.

Mobile heads call `MobileAppBootstrapper.RegisterMobileSync(...)` inside the platform registration delegate created by `MainApplication.CreateAppBuilder` (Android) or `AppDelegate.CreateAppBuilder` (iOS). The bootstrapper also registers `IHttpApiService`.

## DI Composition

Each head passes a typed `PlatformRoot` into the Avalonia application factory.
It carries capabilities, input capabilities, launch preferences, app paths, and
a platform registration delegate. The mobile native hosts override
`CreateAppBuilder` to supply that factory while retaining native lifetime setup.

1. The head selects its app-data directory and reads `LaunchPreferences` once.
   The snapshot supplies theme and layout and seeds runtime preferences.
2. `AppComposition.AddSharedServices` registers the root's `IAppPaths`, clock and
   environment, calls the platform registration delegate, then registers the
   shared graph. Desktop delegates use `RegisterDesktopSync`; mobile delegates
   use `RegisterMobileSync`. Video capability must agree with player registration.
3. `App.OnFrameworkInitializationCompleted` builds the provider with
   `ValidateOnBuild = true` and eagerly resolves subscription owners. The session
   sync applier, pairing server and inbound sync owners are desktop-only; the
   pairing client owner is mobile-only.

Debug developer tools are attached by `DesktopAppBootstrapper` after Avalonia
application setup. There is no shared OS check for them.

The macOS head registers `IWindowBehavior` for its raw Control+Tab input hook and
inert automation peer. The shared window owns attachment cleanup on close. Only
the macOS project enables `AvaloniaAccessUnstablePrivateApis`, allowing direct
raw-input access without reflection; the other heads register no window behavior.
The pure raw-key handling and peer behavior are linked into the app tests without
opting that test project into private APIs.

## Solution Scoping

`Sufni.App.sln` is the full-matrix solution. For day-to-day work the per-platform solutions scope the projects so the IDE only loads what is relevant:

| Solution | Projects scoped |
| --- | --- |
| `Sufni.Desktop.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.Desktop`, `Sufni.App.Video.LibVlc`, the three desktop heads (`Windows`, `macOS`, `Linux`), `Sufni.App.Tests`, `Sufni.Telemetry.Tests` |
| `Sufni.Android.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.Android` |
| `Sufni.iOS.sln` | `Sufni.Telemetry`, `Sufni.Kinematics`, `Sufni.App`, `Sufni.App.iOS` |

The mobile solutions deliberately omit `Sufni.App.Desktop` and the test projects so cold load and incremental build stay fast on the smaller scope.

## Testing

The headless test app (`Sufni.App.Tests/TestSupport/Harness/TestApp.cs`) is a subclass of `App` that skips both XAML loading and the DI bootstrap. Tests configure layout profile and capabilities explicitly when those are the behavior under test. `TestAppEnvironment` provides shared environment setup; view helpers select `UiLayoutProfile` explicitly and gesture tests configure input behavior on their controls.

View-test helpers should select compact/workspace profile and capabilities explicitly rather than assuming platform-based view splits.

## Behavioral Differences

The places where platform behavior still differs are deliberately small:

- **Session detail load**. `SessionDetailViewModel` uses the same local-only staged coordinator path in both profiles: `LoadDetailAsync(...)` publishes decoded telemetry first, then `LoadTrackAsync(...)` settles optional map/track data under the same active load token. Missing processed telemetry or recorded-source payloads surface as an incomplete-local-data screen state; sync is responsible for downloading those payloads before the session is opened. See [UI Workflows § Coordinators](ui-workflows.md#coordinators) and [Sync](sync.md).
- **Sync side**. Desktop-capable platforms can host the server; mobile-capable platforms pair and sync as clients. There is no peer-to-peer mode and no path that runs both on one device unless a future platform registers both capabilities deliberately.
- **Platform-only actions**. Sync server actions, pairing client actions, mass-storage import, storage-provider import, and video playback are capability-gated. Haptics require a registered service; native windowing follows the application lifetime.
- **Dialogs**. `DialogService` shows generic prompts as standalone Avalonia `Window`s when the native lifetime has an owner window, and as in-tree overlays when the lifetime is single-view.
- **Zoomed plot modal**. Double-clicking or double-tapping a plot opens the same live plot control in `PlotZoomOverlayHost`. Workspace profile shows a framed modal over a scrim; compact profile uses an edge-to-edge modal inside the safe area and rotates plot content in portrait-shaped windows. Escape close follows keyboard input capability, and single-view hosts participate in the hardware-back chain through `TryCloseTransientShellSurface`.
- **Bike linkage editing**. `BikeEditorDesktopView` sets `BikeEditorViewModel.CanChangeRearSuspensionMode`, so the gate currently follows the workspace view rather than an input capability. Moving that behavior to an input capability remains adoption work.

Anything else (entity editing, save / conflict semantics, sensor calibration, shell navigation semantics, session detail loading, and plot model rendering) goes through the same coordinators, services, stores, and workspace model on every platform.
