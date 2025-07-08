using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface ISynchronizationServerService
{
    public Task StartAsync(int port = 1557);
}