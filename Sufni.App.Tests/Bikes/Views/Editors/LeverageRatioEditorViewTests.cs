using System.Linq;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sufni.App.Bikes.ViewModels.Editors.Bike;
using Sufni.App.Bikes.Views.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Bikes.Views.Editors;

[Collection("Ui")]
public class LeverageRatioEditorViewTests
{
    [AvaloniaFact]
    public async Task ClearButton_UpdatesCurveImmediatelyAndDebouncesAnalysis()
    {
        var clock = new FakeTimeProvider();
        var coordinator = TestCoordinatorSubstitutes.Bike();
        var snapshot = TestSnapshots.LeverageRatioBike(TestSnapshots.LeverageRatioCurve((0, 0), (10, 25)));
        var editor = new BikeEditorViewModel(snapshot, false, coordinator, Substitute.For<IBikeDependencyQuery>(),
            Substitute.For<IShellCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(),
            TestAppEnvironment.Create(UiLayoutProfile.Workspace).Input, new InlineBackgroundTaskRunner(), clock);
        var view = new LeverageRatioEditorView { DataContext = editor.LeverageRatioEditor };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        coordinator.ClearReceivedCalls();
        try
        {
            var button = view.FindControl<Button>("ClearButton")!;
            Assert.Same(editor.LeverageRatioEditor.ClearCommand, button.Command);
            button.Command!.Execute(null);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.True(view.FindControl<TextBlock>("EmptyStateTextBlock")!.IsVisible);
            await coordinator.DidNotReceive().LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>());
            clock.Advance(TimeSpan.FromMilliseconds(200));
            await editor.LoadedCommand.ExecuteAsync(null).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            await coordinator.Received(1).LoadAnalysisAsync(Arg.Is<RearSuspensionSpec>(spec => spec is RearSuspensionSpec.LeverageRatioDraft), Arg.Any<CancellationToken>());
        }
        finally
        {
            host.Close();
            await editor.PrepareCloseAsync();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task LeverageRatioEditorView_ShowsSummaryAndPoints_WhenValuePresent()
    {
        var viewModel = new LeverageRatioEditorViewModel(canEdit: true);
        viewModel.ReplaceState(TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 45)));
        var view = new LeverageRatioEditorView
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            Assert.True(view.FindControl<Grid>("SummaryGrid")!.IsVisible);
            Assert.False(view.FindControl<TextBlock>("EmptyStateTextBlock")!.IsVisible);
            Assert.Equal("3", view.FindControl<TextBlock>("PointCountTextBlock")!.Text);
            Assert.Equal("20", view.FindControl<TextBlock>("MaxShockStrokeTextBlock")!.Text);
            Assert.Equal("45", view.FindControl<TextBlock>("MaxWheelTravelTextBlock")!.Text);

            var pointsControl = view.FindControl<ItemsControl>("PointsItemsControl");
            Assert.NotNull(pointsControl);
            Assert.Equal(3, pointsControl!.ItemsSource!.Cast<object>().Count());

            Assert.True(view.FindControl<Grid>("EditorButtonsGrid")!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task LeverageRatioEditorView_ShowsEmptyState_WhenNoCurveIsLoaded()
    {
        var view = new LeverageRatioEditorView
        {
            DataContext = new LeverageRatioEditorViewModel(canEdit: false)
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            Assert.False(view.FindControl<Grid>("SummaryGrid")!.IsVisible);
            Assert.True(view.FindControl<TextBlock>("EmptyStateTextBlock")!.IsVisible);
            Assert.False(view.FindControl<ItemsControl>("PointsItemsControl")!.IsVisible);
            Assert.False(view.FindControl<Grid>("EditorButtonsGrid")!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}