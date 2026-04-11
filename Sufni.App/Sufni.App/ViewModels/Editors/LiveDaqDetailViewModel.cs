using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Minimal shell-routable detail tab used until the live session view
/// model is expanded in the later transport/detail phases.
/// </summary>
public sealed partial class LiveDaqDetailViewModel : TabPageViewModelBase
{
    public string IdentityKey { get; }
    public string? BoardId { get; }
    public string? Endpoint { get; }

    public LiveDaqDetailViewModel()
    {
        IdentityKey = string.Empty;
    }

    public LiveDaqDetailViewModel(
        LiveDaqSnapshot snapshot,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        IdentityKey = snapshot.IdentityKey;
        BoardId = snapshot.BoardId;
        Endpoint = snapshot.Endpoint;
        Name = snapshot.DisplayName;
    }
}