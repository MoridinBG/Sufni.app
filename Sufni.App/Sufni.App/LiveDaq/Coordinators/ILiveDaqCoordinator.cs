using System.Threading.Tasks;

namespace Sufni.App.LiveDaq.Coordinators;

public interface ILiveDaqCoordinator
{
    void Activate();

    void Deactivate();

}
