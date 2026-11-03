using Sufni.App.Shell.ViewModels.Base;
using Sufni.App.Setups.Queries;
using Sufni.App.Shell.Coordinators;
using System;
using System.Threading.Tasks;

using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Base;
namespace Sufni.App.Setups.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="SetupSnapshot"/> for use
/// inside a list. Row view models are cheap, non-editable and refresh
/// themselves via <see cref="Update"/> when the underlying snapshot
/// changes. <see cref="OpenPage"/> routes through
/// <see cref="IShellCoordinator"/>; <see cref="UndoableDelete"/> hands
/// the row back to the owning list view model via the
/// <c>requestDelete</c> callback so the list can run its pending-delete
/// undo window before finalizing.
/// </summary>
public sealed class SetupRowViewModel : ListItemRowViewModelBase
{
    private readonly IShellCoordinator shell;
    private readonly Action<SetupRowViewModel> requestDelete;

    public Guid Id { get; private set; }

    public Guid? BoardId
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public SetupRowViewModel(
        SetupDetails details,
        IShellCoordinator shell,
        Action<SetupRowViewModel> requestDelete)
    {
        this.shell = shell;
        this.requestDelete = requestDelete;
        Update(details);
    }

    public void Update(SetupDetails details)
    {
        var snapshot = details.Setup;
        Id = snapshot.Id;
        Name = snapshot.Name;
        BoardId = details.BoardId;
        Timestamp = null;
        IsComplete = true;
    }

    protected override Task OpenPageAsync()
    {
        shell.OpenEditor(new EditorKey.Setup(Id));
        return Task.CompletedTask;
    }

    protected override void UndoableDelete()
    {
        requestDelete(this);
    }
}
