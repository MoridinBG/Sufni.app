using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Sufni.App.Terrain.Views;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Views;

[Collection("Ui")]
public class TerrainClassFilterViewTests
{
    [AvaloniaFact]
    public async Task CompactHeaderOpensSixChipsAndTheBannerClearsTheSelection()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var profile = TerrainProfiles.Classified(TerrainProfiles.Window(0, RoughnessClass.Rough, RoughnessSource.Both));
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, profile)));
        await h.LoadAsync(editor);
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var header = new TerrainClassFilterView { DataContext = editor.SignalsWorkspace.TerrainFilter, Compact = true };
        var banner = new TerrainClassFilterBannerView { DataContext = editor.AnalysisWorkspace.TerrainFilter };
        await using var mounted = await ViewTestHelpers.MountAsync(new StackPanel { Children = { header, banner } });
        Assert.False(banner.IsVisible);
        var button = header.FindControl<Button>("FilterButton")!;
        Click(button);
        await ViewTestHelpers.FlushDispatcherAsync();
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        Assert.True(flyout.IsOpen);
        var chips = Assert.IsType<StackPanel>(flyout.Content).GetVisualDescendants().OfType<ToggleButton>().ToArray();
        Assert.Equal(6, chips.Length);
        var rough = editor.SignalsWorkspace.TerrainFilter!.Roughness[(int)RoughnessClass.Rough];
        Assert.Single(chips, chip => ReferenceEquals(chip.DataContext, rough)).Command!.Execute(null);
        flyout.Hide();
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(banner.IsVisible);
        Click(Assert.Single(banner.GetVisualDescendants().OfType<Button>()));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(banner.IsVisible);
        Assert.False(rough.IsChecked);
        banner.DataContext = null;
        Assert.False(banner.IsVisible);
        await h.CloseAsync(editor);

        void Click(Button control)
        {
            var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), mounted.Host)!.Value;
            mounted.Host.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            mounted.Host.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        }
    }
}
