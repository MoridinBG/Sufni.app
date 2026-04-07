using System;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Surface that the shared list-row controls
/// (<c>DeletableListItemButton</c>, <c>SwipeToDeleteButton</c>,
/// <c>PairedDeviceListItemButton</c>) bind against. Implemented by
/// <c>BikeRowViewModel</c>, <c>SetupRowViewModel</c>,
/// <c>SessionRowViewModel</c>, and <c>PairedDeviceRowViewModel</c> so a
/// single <c>x:DataType</c> resolves for every list page. Each
/// concrete row also exposes a typed <c>Id</c> field (<see cref="Guid"/>
/// for the entity rows, <see cref="string"/> <c>DeviceId</c> for paired
/// devices) for routing through coordinators, but it is not part of
/// this shared surface — list controls only need <see cref="Name"/>,
/// <see cref="Timestamp"/>, <see cref="IsComplete"/>, and the commands.
///
/// The commands are exposed as non-generic <see cref="IRelayCommand"/>
/// so different implementations can use different parameter shapes —
/// the controls always invoke them with the row instance as
/// <c>CommandParameter</c> and the implementation chooses what to do
/// with it.
/// </summary>
public interface IListItemRow
{
    string? Name { get; }
    DateTime? Timestamp { get; }
    bool IsComplete { get; }

    IRelayCommand OpenPageCommand { get; }
    IRelayCommand UndoableDeleteCommand { get; }
    IRelayCommand FakeDeleteCommand { get; }
}
