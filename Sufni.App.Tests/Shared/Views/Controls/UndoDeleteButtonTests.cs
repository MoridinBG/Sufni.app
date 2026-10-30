using NSubstitute;
using System.Reactive.Linq;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sufni.App.Shared.Base;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shared.Views.Controls;

[Collection("Ui")]
public class UndoDeleteButtonTests
{
    [AvaloniaFact]
    public async Task UndoDeleteButton_RendersBar_AndDismissButtonFinalizesDelete()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var owner = CreateOwner("Morning Ride", out var key);
        using var viewModel = new TestPendingDeleteListViewModel(owner);

        var view = new UndoDeleteButton
        {
            DataContext = viewModel
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var dismissButton = Assert.Single(view.FindAllVisual<Button>(), b => b.Name == "DismissButton");
            Assert.Equal("Morning Ride", Assert.Single(viewModel.PendingDeletes).Name);

            dismissButton.Command!.Execute(dismissButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            await owner.Received(1).CommitAsync(key);
            owner.DidNotReceive().Undo(key);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task UndoDeleteButton_UndoButton_CancelsDeleteWithoutFinalizing()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var owner = CreateOwner("Race Setup", out var key);
        using var viewModel = new TestPendingDeleteListViewModel(owner);

        var view = new UndoDeleteButton
        {
            DataContext = viewModel
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var undoButton = Assert.Single(view.FindAllVisual<Button>(), b => b.Name == "UndoButton");

            undoButton.Command!.Execute(undoButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            owner.Received(1).Undo(key);
            await owner.DidNotReceive().CommitAsync(key);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private static IPendingDeletes CreateOwner(string name, out PendingDeleteKey key)
    {
        key = new PendingDeleteKey.Session(Guid.NewGuid());
        var owner = Substitute.For<IPendingDeletes>();
        owner.Observe().Returns(Observable.Return<IReadOnlyList<PendingDelete>>([new(key, name)]));
        return owner;
    }
}
