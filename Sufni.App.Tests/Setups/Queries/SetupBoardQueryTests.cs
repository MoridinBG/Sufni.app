using System.Reactive.Linq;
using DynamicData;
using Sufni.App.Setups.Queries;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Setups.Queries;

public class SetupBoardQueryTests : IDisposable
{
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly SetupStore setups;
    private readonly BoardStore boards;
    private readonly SetupBoardQuery query;

    public SetupBoardQueryTests()
    {
        setups = new(publication);
        boards = new(publication);
        query = new(setups, boards, publication);
    }

    [Fact]
    public async Task BoardOnlyReassignmentAndRemoval_UpdateRowsAndLookups()
    {
        var first = TestSnapshots.Setup(name: "first");
        var second = TestSnapshots.Setup(name: "second");
        await setups.ReplaceAllAsync([first, second]);
        var board = new BoardSnapshot(Guid.NewGuid(), first.Id, 1);
        await boards.PublishBoardsChangedAsync([board]);
        using var subscription = query.Connect().Bind(out var rows).Subscribe();

        await boards.PublishBoardsChangedAsync([board with { SetupId = second.Id, Updated = 2 }]);

        Assert.Null(rows.Single(row => row.Setup.Id == first.Id).BoardId);
        Assert.Equal(board.Id, rows.Single(row => row.Setup.Id == second.Id).BoardId);
        Assert.Equal(board.Id, query.Get(second.Id)!.BoardId);
        Assert.Equal(second, query.FindByBoardId(board.Id));

        await boards.PublishBoardsRemovedAsync([board.Id]);

        Assert.All(rows, row => Assert.Null(row.BoardId));
        Assert.Null(query.FindByBoardId(board.Id));
    }

    [Fact]
    public async Task JoinedBatch_EmitsOnlyCompleteAssociations_AndSetupRemovalClearsLookup()
    {
        var setup = TestSnapshots.Setup();
        var board = new BoardSnapshot(Guid.NewGuid(), setup.Id, 1);
        var observed = new List<SetupDetails>();
        using var subscription = query.Connect().Subscribe(changes =>
            observed.AddRange(changes.Where(change => change.Reason == ChangeReason.Add).Select(change => change.Current)));
        using (publication.BeginBatch())
        {
            await setups.PublishSetupsChangedAsync([setup]);
            await boards.PublishBoardsChangedAsync([board]);
            Assert.Empty(observed);
        }

        Assert.Equal(new SetupDetails(setup, board.Id), Assert.Single(observed));
        await setups.PublishSetupsRemovedAsync([setup.Id]);
        Assert.Null(query.FindByBoardId(board.Id));
        Assert.Null(query.Get(setup.Id));
    }

    [Fact]
    public async Task AdditionalBoardAssignment_NotifiesLookupConsumersEvenWhenDisplayedBoardIsUnchanged()
    {
        var setup = TestSnapshots.Setup();
        var first = new BoardSnapshot(Guid.Parse("00000000-0000-0000-0000-000000000001"), setup.Id, 1);
        var second = new BoardSnapshot(Guid.Parse("00000000-0000-0000-0000-000000000002"), null, 1);
        await setups.PublishSetupsChangedAsync([setup]);
        await boards.PublishBoardsChangedAsync([second, first]);
        var resolved = new List<SetupSnapshot?>();
        using var subscription = query.Changes.Subscribe(_ => resolved.Add(query.FindByBoardId(second.Id)));

        await boards.PublishBoardsChangedAsync([second with { SetupId = setup.Id, Updated = 2 }]);

        Assert.Equal(setup, Assert.Single(resolved));
        Assert.Equal(first.Id, query.Get(setup.Id)!.BoardId);
        Assert.Equal(setup, query.FindByBoardId(first.Id));
    }

    public void Dispose()
    {
        query.Dispose();
        setups.Dispose();
        boards.Dispose();
        publication.Dispose();
    }
}
