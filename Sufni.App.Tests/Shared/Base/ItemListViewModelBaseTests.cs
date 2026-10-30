using Sufni.App.Shared.Models;
using NSubstitute;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Shared.Base;

public class ItemListViewModelBaseTests
{
    [Fact]
    public async Task FailedPendingDelete_RemovesUndoEntryAndShowsError()
    {
        var coordinator = TestCoordinatorSubstitutes.Session();
        coordinator.DeleteAsync(Arg.Any<Guid>()).Returns(new SessionDeleteResult.Failed(SessionDeleteFailureReason.Delete, "failure"));
        using var owner = TestPendingDeletes.Create(sessions: coordinator);
        using var viewModel = new TestPendingDeleteListViewModel(owner);
        var key = new PendingDeleteKey.Session(Guid.NewGuid());
        owner.Schedule(key, "ride");

        await Assert.Single(viewModel.PendingDeletes).FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.PendingDeletes);
        var failure = Assert.Single(viewModel.ErrorMessages);
        Assert.Equal("failure", failure.Details);
        Assert.DoesNotContain("failure", failure.Summary);
    }
}
