using System;
using System.Collections.Generic;
using DynamicData;

namespace Sufni.App.SyncAndPairing.Stores;

/// <summary>Shared recorded board identities and their authored setup associations.</summary>
public interface IBoardStore
{
    IObservable<IChangeSet<BoardSnapshot, Guid>> Connect();
    BoardSnapshot? Get(Guid id);
    IReadOnlyList<BoardSnapshot> GetAll();
}
