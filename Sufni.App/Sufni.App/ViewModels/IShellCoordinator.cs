using System;
using System.Threading.Tasks;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public interface IShellCoordinator
{
    Task DeleteItem(ItemViewModelBase item);
    void UndoableDelete(ItemViewModelBase item);
    void OnBikeAdded(ItemViewModelBase bike);
    void OnSetupAdded(ItemViewModelBase setup);
    Task EvaluateSetupExists();
    bool CanDeleteBike(Guid bikeId);
    void AddBike();
    void AddSetup();
    void OpenImportSessions();
}
