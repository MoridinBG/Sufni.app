using System.Threading.Tasks;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Hosts;

public interface IItemDeletionHost
{
    Task Delete(ItemViewModelBase vm);
    void UndoableDelete(ItemViewModelBase vm);
}
