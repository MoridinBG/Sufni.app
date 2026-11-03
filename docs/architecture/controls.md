# Controls Library

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers reusable Avalonia controls used by the UI layer. Presentation layering and dependency rules live in [UI Architecture](ui.md).

`Sufni.App/Sufni.App/Shared/Views/Controls/` contains reusable UI components: `SearchBar`, `SearchBarWithDateFilter`, `SearchBarCore`, `EditableTitle`, `SwipeToDeleteButton`, `UndoDeleteButton`, `NotificationsBar`, `ErrorMessagesBar`, `ActivityIndicator`, `CommonButtonLine`. Other shared UI lives under `Shared/Views/Overlays/` (`BusyOverlay`, `EditorBusyOverlay`, `PlotZoomOverlayHost`), `Shared/Views/Dialogs/` (`OkCancelDialogWindow`, `YesNoCancelDialogWindow`), and `Shared/DesktopViews/Controls/` (`DeletableListItemButton`).

Slice-owned controls stay with their slices:

- `SidePanel` and `PullableMenuScrollViewer` → `Shell/Views/Controls/`; `PinInput` → `SyncAndPairing/Views/Controls/`.
- Session signal hierarchy controls (`SignalRow`, `SignalRowsRoot`, row-action helpers and recorded rows) → `Sessions/Signals/Views/Controls/`; session pane splitting → `Sessions/Detail/DesktopViews/Controls/`.
- Live signal rows → `LiveDaq/Views/Controls/`; analysis hosts → `Sessions/Analysis/Views/Controls/`.
- `ImportSessionsContentView` is the shared import body. Profile wrappers own profile-specific back and bottom-action behavior.
- `LinearSensorConfigurationView` is the shared view for the fork and shock linear sensor editors. The concrete view models create their own sensor payload types.

Shared control rules:

- `ActivityIndicator` wraps the progress-ring package, so views do not reference that package directly.
- `BusyOverlay` composes spinner, tint, message and progress; it does not own busy state. Its `ShowIndicator` switch hides the spinner and keeps progress and message. Recorded and live session-opening screens use this mode, so full-screen session loading always shows a stage message and a determinate bar.
- Search bars show only paired connection state. Capability-gated shell surfaces show sync activity.
- `CommonButtonLine` binds against `TabPageViewModelBase`. Workspace row controls bind against `ListItemRowViewModelBase` where the row family supports that surface.
- `LiveDaqListItemButton` (`LiveDaq/DesktopViews/Controls/`) binds against `LiveDaqRowViewModel` directly, because live DAQ rows are not deletable and need online/offline presentation.

`ErrorMessagesBar` binds to `ViewModelBase.ErrorMessages`, a collection of
`FailurePresentation` values. Each item renders its summary and, when technical
details are present, a collapsed Details expander with selectable text. Clear
removes the whole collection. Notifications are plain progress and completion text.

`SignalRow` owns reusable signal-row chrome: title, expand/collapse, drag/drop affordances, child-row hosting, row-depth backgrounds, and right-aligned header actions.

- Its plot slot wraps the plot content host in the shared `PlotZoomContainer`, so recorded, live and child signal rows use the same zoom request path.
- A row without a `RowId` (such as a segment comparison row) keeps its declared place, is not stored in the layout preference, and cannot be dragged.
- `SignalRowsRoot` owns the divider handles between visible root rows. It stores completed drags as normalized height ratios in `SessionPreferences.SignalLayout`. A missing ratio for any visible resizable root row resets the group to default sizing.
- Header actions are `SignalRowAction` descriptors that carry command and visual state. The row renders toggle actions first and execute actions second, with extra spacing between the groups. It keeps action pointer input separate from header collapse and drag handling.

`PlotZoomOverlayHost` is the shared top-level zoom modal surface. Desktop
`MainWindow` and mobile `MainView` each place one host above their shell
content, and `App.OnFrameworkInitializationCompleted` registers the active host
with the singleton `IPlotZoomState`. As with the `DialogService` `IDialogHost`
facet, the shell owns the concrete overlay surface, and view models only see a
small transient-surface state interface. On Android hardware
back, `ShellRootViewModel.TryCloseTransientShellSurface()` asks
`IPlotZoomState` to collapse the zoom modal before closing the drawer or
navigating back.

Mobile swipe/delete keeps the Avalonia Labs `Swipe` workaround inside `SwipeToDeleteGestureAdapter`, local to `SwipeToDeleteButton`. `PullableMenuScrollViewer` owns only the pull-menu gesture and transition behavior. Its 200 ms completion uses the inherited UI scheduler and is cancelled on detach. It does not reach into the child swipe control's visual tree.
