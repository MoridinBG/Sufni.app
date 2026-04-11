using System.Threading.Tasks;

namespace Sufni.App.Coordinators;

public interface ILiveDaqCoordinator
{
    void Activate();

    void Deactivate();

    Task SelectAsync(string identityKey);
}