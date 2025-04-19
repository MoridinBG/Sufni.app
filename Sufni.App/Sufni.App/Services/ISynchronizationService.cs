using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface ISynchronizationService
{
    public Task SyncAll();
}
