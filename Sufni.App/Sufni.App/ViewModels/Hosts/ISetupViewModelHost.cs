using System.Threading.Tasks;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Hosts;

public interface ISetupViewModelHost : IItemDeletionHost
{
    void OnSetupSaved(SetupViewModel vm);
    Task AfterSetupSavedAsync();
}
