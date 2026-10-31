using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Stores;

namespace Sufni.App.Setups.Queries;

public sealed record SetupDetails(SetupSnapshot Setup, Guid? BoardId);

/// <summary>Joins setup metadata with board-owned associations.</summary>
public interface ISetupBoardQuery
{
    IObservable<IChangeSet<SetupDetails, Guid>> Connect();
    IObservable<Unit> Changes { get; }
    SetupDetails? Get(Guid setupId);
    SetupSnapshot? FindByBoardId(Guid boardId);
}

internal sealed class SetupBoardQuery : ISetupBoardQuery, IDisposable
{
    private readonly ISetupStore setups;
    private readonly IBoardStore boards;
    private readonly SourceCache<SetupDetails, Guid> rows = new(row => row.Setup.Id);
    private readonly IDisposable subscription;

    public SetupBoardQuery(ISetupStore setups, IBoardStore boards, StorePublication publication)
    {
        this.setups = setups;
        this.boards = boards;
        Changes = publication.Committed.Where(commit => commit.Stores.Contains(setups) || commit.Stores.Contains(boards))
            .Select(_ => Unit.Default);
        subscription = Changes.Subscribe(_ => Refresh());
        Refresh();
    }

    public IObservable<Unit> Changes { get; }
    public IObservable<IChangeSet<SetupDetails, Guid>> Connect() => rows.Connect();

    public SetupDetails? Get(Guid setupId) => setups.Get(setupId) is { } setup
        ? new SetupDetails(setup, ReadBoardAssignments().GetValueOrDefault(setupId)) : null;

    public SetupSnapshot? FindByBoardId(Guid boardId) => boards.Get(boardId)?.SetupId is { } setupId
        ? setups.Get(setupId) : null;

    private Dictionary<Guid, Guid?> ReadBoardAssignments() => boards.GetAll()
        .Where(board => board.SetupId is not null).GroupBy(board => board.SetupId!.Value)
        .ToDictionary(group => group.Key, group => (Guid?)group.Min(board => board.Id));

    private void Refresh()
    {
        var assignments = ReadBoardAssignments();
        rows.EditDiff(setups.GetAll().Select(setup => new SetupDetails(setup, assignments.GetValueOrDefault(setup.Id))),
            EqualityComparer<SetupDetails>.Default);
    }

    public void Dispose()
    {
        subscription.Dispose();
        rows.Dispose();
    }
}
