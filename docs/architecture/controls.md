# Controls Library

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers reusable Avalonia controls used by the UI layer. Presentation layering and dependency rules live in [UI Architecture](ui.md).

`Sufni.App/Sufni.App/Shared/Views/Controls/` contains reusable UI components: `SearchBar`, `SearchBarWithDateFilter`, `SearchBarCore`, `EditableTitle`, `SwipeToDeleteButton`, `UndoDeleteButton`, `SidePanel`, `NotificationsBar`, `ErrorMessagesBar`, `ActivityIndicator`, `CommonButtonLine`, and the shared telemetry row controls (`SignalRow`, `SignalRowsRoot`, and related row-action helpers). Adjacent shared UI concerns live under `Shared/Views/Overlays/` (`BusyOverlay`, `EditorBusyOverlay`), `Shared/Views/Dialogs/` (`OkCancelDialogWindow`, `YesNoCancelDialogWindow`), and `Shared/DesktopViews/Controls/` (`DeletableListItemButton`). Slice-owned controls stay with their slices: `PullableMenuScrollViewer` is under `Shell/Views/Controls/`, `PinInput` under `SyncAndPairing/Views/Controls/`, recorded signal rows under `Sessions/Signals/Views/Controls/`, live signal rows under `LiveDaq/Views/Controls/`, and analysis hosts under `Sessions/Analysis/Views/Controls/`. `ImportSessionsContentView` is the shared mobile/desktop import body; its wrappers keep shell-specific back/bottom-action behavior. `LinearSensorConfigurationView` is the shared view for the fork/shock linear sensor editors while the concrete view models still create their own sensor payload types. `ActivityIndicator` wraps the current progress-ring package so views do not reference that package directly, and `BusyOverlay` standardizes spinner/tint/message/progress composition without owning busy state. Search bars show paired connection state only; sync activity is surfaced by the mobile shell overlay or desktop paired-devices surface. `CommonButtonLine` binds against `TabPageViewModelBase`, while desktop row controls bind against `ListItemRowViewModelBase` where the row family supports that common surface. `LiveDaqListItemButton` is a separate desktop control under `LiveDaq/DesktopViews/Controls/` that binds against `LiveDaqRowViewModel` directly because live DAQ rows are not deletable and need online/offline presentation.

`SignalRow` owns reusable signal-row chrome: title, expand/collapse, drag/drop affordances, child-row hosting, row-depth backgrounds, and right-aligned header actions. `SignalRowsRoot` owns the manual divider handles between visible root rows; completed drags are captured as normalized height ratios in `SessionPreferences.SignalLayout`, and a missing ratio for any visible resizable root row resets the group to default sizing. Header actions are supplied as `SignalRowAction` descriptors and rendered by the row, grouped as toggle actions first and execute actions second with extra spacing between the groups. Action descriptors carry command and visual state; the row keeps action pointer input separate from header collapse and drag handling.

Extension contribution hosts are also reusable controls. The app shell uses
`AppToolbarContributionsView` for app-level toolbar actions. Recorded-session
hosts include:
`RecordedSessionToolbarContributionsView`,
`RecordedSessionMediaPanesView`,
`RecordedSessionAnalysisContributionsView`,
`RecordedSessionSessionListContributionsView`, and
`SignalRowExtensionHost`. They render neutral slot collections
from the app or recorded-session extension surfaces and wrap non-control
contribution view models in `ContentControl` so the extension view registry
can resolve templates normally.

Mobile swipe/delete keeps the Avalonia Labs `Swipe` workaround inside `SwipeToDeleteGestureAdapter`, local to `SwipeToDeleteButton`. `PullableMenuScrollViewer` owns only the pull-menu gesture and transition behavior; it does not reach into the child swipe control's visual tree.
