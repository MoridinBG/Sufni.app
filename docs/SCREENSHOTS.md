# Screenshot Generator Guide

## Purpose

The `Sufni.Screenshots` project captures pixel-accurate screenshots of
Avalonia views in a headless environment. Each screenshot class drives a
view through a sequence of states and saves a PNG at every stage.
No display server, window manager, or manual interaction is required.

## How It Works

The project is an xUnit test project that uses Avalonia Headless with the
real Skia rendering backend. The xUnit runner manages the Avalonia
application lifetime; each `[AvaloniaFact]` method mounts a view inside a
`Window`, flushes the dispatcher so bindings and layout resolve, then calls
`CaptureRenderedFrame()` to grab the rasterized pixels and save them as PNG.

Key pieces:

| File | Role |
|------|------|
| `Infrastructure/ScreenshotApp.axaml` | Application subclass that mirrors `App.axaml` resources and styles |
| `Infrastructure/ScreenshotApp.axaml.cs` | Code-behind; loads the AXAML, skips all DI |
| `Infrastructure/ScreenshotAppBuilder.cs` | Configures `.UseSkia()` + `UseHeadless(UseHeadlessDrawing = false)` |

`ScreenshotApp.axaml` is a standalone copy of every color, brush, and style
from the production `App.axaml`. It must be kept in sync manually when
production resources change. It does **not** load `ViewLocator` because the
screenshot project does not subclass `Sufni.App.App` and views are
instantiated directly.

## Running Screenshots

Generate all screenshots:

```
dotnet test Sufni.Screenshots
```

Generate screenshots for a single scenario class:

```
dotnet test Sufni.Screenshots --filter BikeCreationScreenshots
```

Generate a single step:

```
dotnet test Sufni.Screenshots --filter Step01_NewEmptyBike
```

Output lands in the test assembly's output directory:

```
Sufni.Screenshots/bin/Debug/net10.0/screenshots/<scenario>/
```

## Anatomy of a Screenshot Class

A screenshot class captures one feature workflow. Every `[AvaloniaFact]`
method represents one visual state in that workflow.

```csharp
public class BikeCreationScreenshots
{
    // Derive an output directory from the assembly location.
    private static readonly string OutputDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "screenshots",
        "bike-creation");

    [AvaloniaFact]
    public async Task Step01_NewEmptyBike()
    {
        // 1. Build the snapshot that represents this stage.
        var snapshot = new BikeSnapshot( /* ... */ );

        // 2. Create a view model with mock collaborators.
        var vm = CreateViewModel(snapshot, isNew: true);

        // 3. Mount, render, capture, save.
        await CaptureControlsPanel(vm, "01-new-bike.png");
    }
}
```

### Building Snapshots

Each step builds an immutable snapshot record that represents the data the
user would have entered at that point. Snapshots are not persisted; they are
constructed inline with the values relevant to the stage. Fields the user
has not reached yet stay `null` or at their default.

### Mocking Collaborators

View models require coordinator, query, shell, and dialog-service
dependencies. Mock all of them with NSubstitute:

- Coordinators: return `Unavailable`, `Canceled`, or a no-op result for
  every async method so that no real I/O or navigation runs.
- Queries: return `Observable.Empty<Unit>()` for change streams and
  sensible defaults for lookups (e.g. `IsBikeInUse` returns `false`).
- Shell and dialog service: plain `Substitute.For<T>()` stubs; the
  screenshot never triggers navigation or confirmation dialogs.

### Mounting and Capturing

The capture helper follows a fixed sequence:

```csharp
private static async Task CaptureView(Control view, string filename, int width, int height)
{
    var window = new Window
    {
        Width = width,
        Height = height,
        Content = view
    };

    // Show the window so Avalonia performs layout and template application.
    window.Show();

    // Flush the dispatcher twice to ensure bindings, templates, and any
    // deferred layout passes have fully resolved.
    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    // Grab the rasterized frame from the Skia surface.
    var bitmap = window.CaptureRenderedFrame();

    Directory.CreateDirectory(OutputDir);
    bitmap!.Save(Path.Combine(OutputDir, filename));

    window.Close();
    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
}
```

**Why two flushes?** A single flush guarantees that property-change
notifications have propagated. A second flush covers deferred work such as
template application inside `ContentControl`, `ItemsPresenter` realization,
and asynchronous binding updates that schedule back to the dispatcher.

**Why `CaptureRenderedFrame`?** This is the Avalonia Headless extension that
reads back the Skia surface as a `WriteableBitmap`. It requires the app
builder to have `.UseSkia()` and `UseHeadlessDrawing = false`.

## Writing a New Screenshot Scenario

### 1. Decide What to Capture

Pick the view that best represents the workflow. Options:

- A **desktop-only subview** such as `BikeImageControlsDesktopView` for a
  form-heavy editing panel.
- A **mobile view** such as `BikeEditorView` for a read-only summary
  layout.
- A **desktop composed view** such as `BikeEditorDesktopView` for a
  split-pane layout with canvas and controls.

Subviews render faster and at a predictable size. Prefer them over
full-page compositions unless the screenshot specifically needs to show
page-level structure.

### 2. Create the Scenario Class

Add a new file in `Sufni.Screenshots/`:

```csharp
namespace Sufni.Screenshots;

public class SetupCreationScreenshots
{
    private static readonly string OutputDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "screenshots",
        "setup-creation");

    [AvaloniaFact]
    public async Task Step01_EmptySetup()
    {
        var snapshot = new SetupSnapshot( /* minimal values */ );
        var vm = CreateViewModel(snapshot, isNew: true);

        var view = new SetupEditorView { DataContext = vm };
        // ... mount and capture
    }

    // Step02, Step03, etc.
}
```

Name each method `StepNN_Description` so that tests sort in workflow order.

### 3. Choose the Right View Model Constructor

Read the target view model's constructor to know which collaborators it
needs. The pattern is always:

1. A snapshot record with the data for this stage.
2. An `isNew` flag (`true` for a brand-new entity, `false` for editing an
   existing one). With `isNew: false` the VM calls `refreshAnalysis` in its
   constructor, so mock the coordinator's analysis method.
3. Coordinator, query, shell, dialog — all mocked.

### 4. Handle Images

Avalonia bitmaps cannot be decoded from a stream when using the Skia
headless backend with certain tiny or unusual image formats. Use
`WriteableBitmap` for placeholder images:

```csharp
private static WriteableBitmap PlaceholderImage(int width = 100, int height = 60)
{
    return new WriteableBitmap(
        new PixelSize(width, height),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Premul);
}
```

This creates a transparent bitmap of the given size without touching the
PNG/JPEG decoder. Use a real image file only if the screenshot needs to show
actual image content — in that case, embed it as an `AvaloniaResource` in
the screenshot project and load it through `AssetLoader`.

### 5. Pick Window Dimensions

Choose dimensions that match the target layout:

| Target | Suggested Size |
|--------|---------------|
| Mobile view (single column) | 400 x 800 |
| Desktop controls panel | 420 x 900 |
| Desktop split view | 1200 x 800 |
| Full desktop window | 1400 x 900 |

Wider windows may produce empty space; narrower windows may clip or wrap
controls. Adjust per scenario.

### 6. Verify the Output

After running the scenario, open each PNG and check:

- Text is rendered and readable.
- Bound values match the snapshot.
- Conditional sections (visibility bindings) appear or hide as expected.
- The dark theme and custom resources are applied (not Fluent defaults).

If a section renders as empty or a control shows a fallback, the
`ScreenshotApp.axaml` may be missing a resource, style, or style include
that the view depends on. Add it there.

## Keeping ScreenshotApp.axaml in Sync

`ScreenshotApp.axaml` is a standalone copy of the production `App.axaml`
resources and styles. When the production file changes, update the
screenshot copy to match. The sections to watch:

- `Application.Resources` — color and brush definitions.
- `Application.Styles` — `FluentTheme`, `ControlThemes`, style includes
  for plots, progress ring, data grid, and all global selector styles.

If a new style include is added to `App.axaml`, add the same include to
`ScreenshotApp.axaml`. If a new color resource is added, add it as well.

## Capturing Desktop vs Mobile Variants

The production app uses `App.Current.IsDesktop` (via `ViewLocator`) to
select between desktop and mobile views. The screenshot project bypasses
`ViewLocator` entirely — views are constructed directly. To screenshot a
mobile view, instantiate the mobile view class; to screenshot a desktop
view, instantiate the desktop view class. No `IsDesktop` flag is involved.

## Capturing Intermediate Async States

To screenshot a loading or busy state, hold the async operation pending
with a `TaskCompletionSource<T>`:

```csharp
[AvaloniaFact]
public async Task Step_ShowsLoadingState()
{
    var tcs = new TaskCompletionSource<BikeEditorAnalysisResult>();

    var coordinator = Substitute.For<IBikeCoordinator>();
    coordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
        .Returns(tcs.Task); // never completes during the test

    var vm = new BikeEditorViewModel(snapshot, isNew: false, coordinator, /* ... */);

    var view = new BikeImageControlsDesktopView { DataContext = vm };
    var window = ShowAndFlush(view);

    // The view model is now waiting for analysis — IsPlotBusy should be true.
    // Capture the loading/busy state.
    window.CaptureRenderedFrame()!.Save(Path.Combine(OutputDir, "loading.png"));

    // Complete the operation so cleanup runs.
    tcs.SetResult(new BikeEditorAnalysisResult.Unavailable());

    window.Close();
    await FlushAsync();
}
```

## Troubleshooting

### `CaptureRenderedFrame` returns `null`

The app builder is missing `.UseSkia()` or has `UseHeadlessDrawing = true`.
Both are required for pixel capture:

```csharp
AppBuilder.Configure<ScreenshotApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
```

### `TypeLoadException` mentioning `Avalonia.Skia`

Version mismatch between `Avalonia` and `Avalonia.Skia`. Pin `Avalonia.Skia`
in `Directory.Packages.props` to the same version as the core `Avalonia`
package and reference it in the screenshot project's `.csproj`.

### `Unable to locate IPlatformRenderInterface`

Same root cause as above — `.UseSkia()` is missing or the Skia assembly
failed to load. Check the package reference and version.

### View renders with Fluent defaults instead of dark theme

`ScreenshotApp.axaml` is missing the resource or style that the view
depends on. Compare it with `App.axaml` and add the missing entry.

### `Bitmap` constructor throws in Skia mode

Skia's PNG decoder rejects certain minimal or unusual images. Use
`WriteableBitmap` for placeholder images (see the Handle Images section).

### A control appears but is empty

The control likely depends on a `DataTemplate` registered in
`Application.DataTemplates` (via `ViewLocator` in production). Since the
screenshot app does not register `ViewLocator`, any `ContentControl` that
resolves its child through data-template lookup will be empty. Either
set the child view directly in the test, or add a targeted `DataTemplate`
to `ScreenshotApp.axaml` for the specific view model type.
