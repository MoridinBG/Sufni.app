using System;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Stores;

public sealed record BoardSnapshot(Guid Id, Guid? SetupId, long Updated)
{
    public static BoardSnapshot From(Board board) => new(board.Id, board.SetupId, board.Updated);
}
