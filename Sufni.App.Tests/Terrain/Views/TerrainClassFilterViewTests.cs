using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Sufni.App.Infrastructure.Theming;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.Views.SessionPages;
using Sufni.App.Terrain.Views;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.LiveDaq;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Views;

[Collection("Ui")]
public class TerrainClassFilterViewTests
{
    [AvaloniaFact]
    public async Task CompactHeaderEntryChoosesAScope()
    {
        using var h = new SessionDetailHarness();
        var editor = await LoadAsync(h);
        var filter = editor.SignalsWorkspace.TerrainScope!;
        var header = new TerrainClassFilterView { DataContext = filter, Compact = true };
        await using var mounted = await ViewTestHelpers.MountAsync(header);
        var button = header.FindControl<Button>("FilterButton")!;
        Click(mounted.Host, button);
        await ViewTestHelpers.FlushDispatcherAsync();
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        Assert.True(flyout.IsOpen);
        var rough = filter.Roughness[(int)RoughnessClass.Rough];
        Assert.Single(Assert.IsType<StackPanel>(flyout.Content).GetVisualDescendants().OfType<ToggleButton>(),
            chip => ReferenceEquals(chip.DataContext, rough)).Command!.Execute(null);
        Assert.False(filter.IsDefault);
        flyout.Hide();
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task LegendEntryShowsItsScaleColourAndSelectsItsClassWithinThePreset()
    {
        using var h = new SessionDetailHarness();
        var editor = await LoadAsync(h);
        var scope = editor.SignalsWorkspace.TerrainScope!;
        var legend = new TerrainLegendView { DataContext = scope, Options = scope.Roughness };
        await using var mounted = await ViewTestHelpers.MountAsync(legend);
        var entries = legend.GetVisualDescendants().OfType<ToggleButton>().ToArray();
        var rough = Assert.Single(entries, e => ReferenceEquals(e.DataContext, scope.Roughness[(int)RoughnessClass.Rough]));
        Assert.False(Assert.Single(entries, e => ReferenceEquals(e.DataContext, scope.Roughness[(int)RoughnessClass.TechnicalClimb])).IsEnabled);
        var swatch = Assert.Single(rough.GetVisualDescendants().OfType<Border>(), b => b.Background is ISolidColorBrush);
        Assert.Equal(SufniThemes.SignalSeries.Terrain.Roughness[(int)RoughnessClass.Rough], ((ISolidColorBrush)swatch.Background!).Color);
        rough.Command!.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(rough.IsChecked);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task BannerDefaultButtonRestoresTheDefaultScope()
    {
        using var h = new SessionDetailHarness();
        var editor = await LoadAsync(h);
        var banner = new TerrainClassFilterBannerView { DataContext = editor.AnalysisWorkspace.TerrainScope };
        await using var mounted = await ViewTestHelpers.MountAsync(banner);
        var scope = editor.AnalysisWorkspace.TerrainScope!;
        var button = Assert.Single(banner.GetVisualDescendants().OfType<Button>());
        Assert.False(button.IsVisible);
        var rough = scope.Roughness[(int)RoughnessClass.Rough];
        rough.Select.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();
        Click(mounted.Host, button);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(scope.IsDefault);
        Assert.False(rough.IsChecked);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task BannerIsHiddenOnALiveSessionPage()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();
        var context = LiveCaptureTestData.Context();
        using var bikes = new BikeStoreStub(TestSnapshots.Bike(context.BikeId));
        using var workspace = new LiveSessionAnalysisWorkspaceViewModel(context, Substitute.For<ILiveCaptureCoordinator>(),
            bikes, TestCoordinatorSubstitutes.Bike(), _ => { });
        workspace.Apply(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed()));
        var page = new VibrationPageView { DataContext = new VibrationPageViewModel(workspace) };
        await using var mounted = await ViewTestHelpers.MountAsync(page);
        Assert.False(Assert.Single(page.GetVisualDescendants().OfType<TerrainClassFilterBannerView>()).IsVisible);
    }

    private static async Task<SessionDetailViewModel> LoadAsync(SessionDetailHarness h)
    {
        var session = h.CreateRecordedSession();
        var profile = TerrainProfiles.Classified(TerrainProfiles.Window(0, RoughnessClass.Rough, RoughnessSource.Both));
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, profile)));
        await h.LoadAsync(editor);
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        return editor;
    }

    private static void Click(Window host, Button control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), host)!.Value;
        host.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        host.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
    }
}
