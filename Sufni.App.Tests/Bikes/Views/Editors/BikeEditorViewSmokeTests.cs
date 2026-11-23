using Microsoft.Extensions.Time.Testing;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.Kinematics;

using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.Bikes.DesktopViews.Editors;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Views.Editors;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Views.Editors;

[Collection("Ui")]
public class BikeEditorViewSmokeTests : BikeEditorFixture
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopModeButton_FollowsPointerCapability(bool hasPointer)
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();
        var view = new BikeEditorDesktopView { DataContext = CreateEditor(FullSuspensionSnapshot(), hasPointer: hasPointer) };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            var selector = view.GetVisualDescendants().OfType<RearSuspensionModeSelectorDesktopView>().Single();
            Assert.Equal(hasPointer, selector.FindControl<ToggleButton>("LinkageModeButton")!.IsEnabled);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task BikeEditorView_HidesShockStrokeAndPlot_WhenBikeIsHardtail()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new BikeEditorView
        {
            DataContext = CreateEditor(TestSnapshots.Bike())
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();

        var frame = view.GetVisualDescendants().OfType<BikeFrameSummaryView>().Single();
        Assert.False(frame.FindControl<TextBlock>("ShockStrokeLabel")!.IsVisible);
        Assert.False(view.FindControl<BikeAnalysisPlotView>("AnalysisPlot")!.IsVisible);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task FrameFieldsRoundDisplayedValuesWithoutEditingTheDraft()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var snapshot = TestSnapshots.Bike() with { HeadAngle = 62.2987 };
        var editor = CreateEditor(snapshot);
        var view = new BikeFrameFieldsDesktopView { DataContext = editor.Frame };
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        var field = view.FindControl<NumericUpDown>("HeadAngleInput")!;

        Assert.Equal(62.3m, decimal.Parse(field.Text!, field.NumberFormat));
        Assert.Equal(snapshot.HeadAngle, editor.Frame.HeadAngle);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task HeadAngleFieldBecomesEditableWhenPhotoCalibrationIsRemoved()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var editor = CreateEditor(FullSuspensionSnapshot(true, frontWheelDiameter: 702, rearWheelDiameter: 700));
        var view = new BikeFrameFieldsDesktopView { DataContext = editor.Frame };
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        var field = view.FindControl<NumericUpDown>("HeadAngleInput")!;
        Assert.True(field.IsReadOnly);
        Assert.False(field.ShowButtonSpinner);
        var calculated = field.Value;
        var calibratedHint = Assert.IsType<string>(ToolTip.GetTip(field));

        editor.Linkage.Chainstay = null;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(field.IsReadOnly);
        Assert.True(field.ShowButtonSpinner);
        Assert.Equal(calculated, field.Value);
        var manualHint = Assert.IsType<string>(ToolTip.GetTip(field));
        Assert.False(string.IsNullOrWhiteSpace(manualHint));
        Assert.NotEqual(calibratedHint, manualHint);
    }

    [AvaloniaFact]
    public async Task WheelEditorTracksLinkageAndShockStrokeAvailability()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var editor = CreateEditor(FullSuspensionSnapshot());
        var view = new BikeWheelsEditorDesktopView { DataContext = editor.Wheels };
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        Assert.True(view.IsVisible);

        editor.Frame.ShockStroke = null;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(view.IsVisible);
    }

    [AvaloniaFact]
    public async Task LinkageGridHonorsJointAndShockEditRules()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var editor = CreateEditor(FullSuspensionSnapshot());
        var view = new LinkageControlsDesktopView { DataContext = editor.Linkage };
        view.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Sufni.App/"))
            { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        var joints = view.FindControl<DataGrid>("JointGrid")!;
        var links = view.FindControl<DataGrid>("LinkGrid")!;

        async Task<bool> Begin(DataGrid grid, object item, int column)
        {
            grid.SelectedItem = item;
            grid.CurrentColumn = grid.Columns[column];
            grid.ScrollIntoView(item, grid.CurrentColumn);
            await ViewTestHelpers.FlushDispatcherAsync();
            grid.Focus();
            return grid.BeginEdit();
        }

        var eye = editor.Linkage.JointViewModels.Single(joint => joint.Name == JointNames.ShockEye1);
        var wheel = editor.Linkage.JointViewModels.Single(joint => joint.Type == JointType.RearWheel);
        Assert.False(await Begin(joints, wheel, 1));
        Assert.False(await Begin(joints, eye, 0));
        Assert.True(await Begin(joints, eye, 1));
        joints.CancelEdit();
        Assert.False(await Begin(links, editor.Linkage.LinkViewModels.Single(link => link.IsImmutable), 0));
    }
}
