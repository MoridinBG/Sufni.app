using System.Linq;
using System.Windows.Input;
using Sufni.App.Shell.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.DesktopViews.ItemLists;
using Sufni.App.Shell.Coordinators;
using Microsoft.Extensions.Time.Testing;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;

using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.Bikes.ViewModels.Rows;
using Sufni.App.Bikes.Views.ItemLists;
using Sufni.App.Shell.Views.Controls;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Bikes.Views.ItemLists;

[Collection("Ui")]
public class BikeListViewTests
{
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAction_UsesListCommandAndOpensNewEditor(bool desktop)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var coordinator = TestCoordinatorSubstitutes.Bike();
        var draft = BikeDraft.From(TestSnapshots.Bike());
        coordinator.ImportBikeAsync(Arg.Any<System.Threading.CancellationToken>()).Returns(new BikeImportResult.Imported(draft));
        using var viewModel = new BikeListViewModel(new BikeStoreStub(), Substitute.For<IBikeDependencyQuery>(), coordinator,
            new InlineUiThreadDispatcher(), TestPendingDeletes.Create(bikes: coordinator, shell: shell),
            new FakeTimeProvider(), shell, TestShellFeedback.Create());
        UserControl view = desktop ? new BikeListDesktopView() : new BikeListView();
        view.DataContext = viewModel;
        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);
        ICommand command = desktop
            ? view.FindControl<Button>("ImportButton")!.Command!
            : Assert.Single(mounted.Control.FindFirstVisual<PullableMenuScrollViewer>()!
                .MenuItems.Cast<PullMenuItemViewModel>(), item => item.Name == "import").Command;

        Assert.Same(viewModel.ImportCommand, command);
        await ((IAsyncRelayCommand)command).ExecuteAsync(null);

        shell.Received(1).OpenEditor(new EditorKey.NewBike(draft));
    }

    [AvaloniaFact]
    public async Task BikeListView_RendersBoundRows_AndOpensSelectedBike()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Bike(name: "Trail Bike");
        var store = new BikeStoreStub(snapshot);
        var coordinator = TestCoordinatorSubstitutes.Bike();
        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();
        dependencyQuery.IsBikeInUse(snapshot.Id).Returns(false);
        dependencyQuery.Changes.Returns(Observable.Return(Unit.Default));

        var viewModel = new BikeListViewModel(store, dependencyQuery, coordinator, new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(bikes: coordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new BikeListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.NotNull(mounted.Control.FindFirstVisual<PullableMenuScrollViewer>());
        var row = Assert.Single(mounted.Control.FindAllVisual<SwipeToDeleteButton>());
        var rowViewModel = Assert.IsType<BikeRowViewModel>(row.DataContext);

        Assert.Equal("Trail Bike", rowViewModel.Name);
        rowViewModel.OpenPageCommand.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        shell.Received(1).OpenEditor(new EditorKey.Bike(snapshot.Id));
    }

    [AvaloniaFact]
    public async Task BikeListView_RendersNoRows_WhenStoreIsEmpty()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var store = new BikeStoreStub();
        var coordinator = TestCoordinatorSubstitutes.Bike();
        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();
        dependencyQuery.Changes.Returns(Observable.Return(Unit.Default));

        var view = new BikeListView
        {
            DataContext = new BikeListViewModel(store, dependencyQuery, coordinator, new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(bikes: coordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create()),
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.Empty(mounted.Control.FindAllVisual<SwipeToDeleteButton>());
    }
}
