using Sufni.App.Shell.Coordinators;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;

using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Setups.ViewModels.Rows;
using Sufni.App.Setups.Views.ItemLists;
using Sufni.App.Shell.Views.Controls;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Setups.Views.ItemLists;

[Collection("Ui")]
public class SetupListViewTests
{
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    [AvaloniaFact]
    public async Task SetupListView_RendersBoundRows_AndOpensSelectedSetup()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Setup(name: "Race Setup");
        var store = new SetupBoardQueryStub(new Sufni.App.Setups.Queries.SetupDetails(snapshot, null));
        var coordinator = TestCoordinatorSubstitutes.Setup();

        var viewModel = new SetupListViewModel(store, new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(setups: coordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SetupListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.NotNull(mounted.Control.FindFirstVisual<PullableMenuScrollViewer>());
        var row = Assert.Single(mounted.Control.FindAllVisual<SwipeToDeleteButton>());
        var rowViewModel = Assert.IsType<SetupRowViewModel>(row.DataContext);

        Assert.Equal("Race Setup", rowViewModel.Name);
        rowViewModel.OpenPageCommand.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        shell.Received(1).OpenEditor(new EditorKey.Setup(snapshot.Id));
    }
}
