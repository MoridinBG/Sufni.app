using System;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Surface that the shared list-row controls
/// (<c>DeletableListItemButton</c>, <c>SwipeToDeleteButton</c>)
/// bind against. Both the legacy <c>ItemViewModelBase</c> and the
/// new snapshot-backed row view models (<c>BikeRowViewModel</c>,
/// <c>SetupRowViewModel</c>, …) implement this so a single
/// <c>x:DataType</c> resolves for every list page.
///
/// The commands are exposed as non-generic <see cref="IRelayCommand"/>
/// so different implementations can use different parameter shapes —
/// the controls always invoke them with the row instance as
/// <c>CommandParameter</c> and the implementation chooses what to do
/// with it.
/// </summary>
public interface IListItemRow
{
    Guid Id { get; }
    string? Name { get; }
    DateTime? Timestamp { get; }
    bool IsComplete { get; }

    IRelayCommand OpenPageCommand { get; }
    IRelayCommand UndoableDeleteCommand { get; }
    IRelayCommand FakeDeleteCommand { get; }
}
