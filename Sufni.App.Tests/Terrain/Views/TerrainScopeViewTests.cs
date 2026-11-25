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
using Sufni.App.Terrain.Models;
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
public class TerrainScopeChipViewTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChipFlyoutOffersThePresetsAndTheLegendsOnlyWhenAsked(bool showLegends)
    {
        using var h = new SessionDetailHarness();
        var editor = await LoadAsync(h);
        var scope = editor.SignalsWorkspace.TerrainScope!;
        var chip = new TerrainScopeChipView { DataContext = scope, ShowLegends = showLegends };
        await using var mounted = await ViewTestHelpers.MountAsync(chip);
        Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Descents · 10 m");
        var button = chip.FindControl<Button>("ScopeButton")!;
        Click(mounted.Host, button);
        await ViewTestHelpers.FlushDispatcherAsync();
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        var content = Assert.IsType<StackPanel>(flyout.Content);
        Assert.Contains(content.GetVisualDescendants().OfType<ToggleButton>(), t => ReferenceEquals(t.DataContext, scope.Presets[0]));
        Assert.Equal(showLegends, content.GetVisualDescendants().OfType<TerrainLegendView>().Any(l => l.IsEffectivelyVisible));
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
    public async Task BannerStaysQuietForTheDefaultScopeAndStandsOutForAChosenOne()
    {
        using var h = new SessionDetailHarness();
        var editor = await LoadAsync(h);
        var scope = editor.AnalysisWorkspace.TerrainScope!;
        var banner = new TerrainScopeBannerView { DataContext = scope };
        await using var mounted = await ViewTestHelpers.MountAsync(banner);
        var border = banner.FindControl<Border>("Banner")!;
        Assert.True(banner.IsVisible);
        Assert.Null(border.Background);
        scope.Roughness[(int)RoughnessClass.Rough].Select.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.NotNull(border.Background);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task EmptyScopeOffersToUseAllGround()
    {
        using var h = new SessionDetailHarness();
        var editor = await LoadAsync(h);
        var scope = editor.AnalysisWorkspace.TerrainScope!;
        var banner = new TerrainScopeBannerView { DataContext = scope };
        await using var mounted = await ViewTestHelpers.MountAsync(banner);
        var useAll = Assert.Single(banner.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Use all"));
        Assert.False(useAll.IsVisible);
        scope.Presets[(int)TerrainPreset.Climbs].Select.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();
        Click(mounted.Host, useAll);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(scope.Presets[(int)TerrainPreset.All].IsChecked);
        Assert.False(scope.IsEmpty);
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
        var page = new StrokesPageView { DataContext = new StrokesPageViewModel(workspace) };
        await using var mounted = await ViewTestHelpers.MountAsync(page);
        Assert.False(Assert.Single(page.GetVisualDescendants().OfType<TerrainScopeBannerView>()).IsVisible);
    }

    private static async Task<SessionDetailViewModel> LoadAsync(SessionDetailHarness h)
    {
        var session = h.CreateRecordedSession();
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0, RoughnessClass.Rough)]);
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
