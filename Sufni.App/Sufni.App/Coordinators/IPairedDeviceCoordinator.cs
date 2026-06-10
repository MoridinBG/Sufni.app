using System.Threading.Tasks;

namespace Sufni.App.Coordinators;

public interface IPairedDeviceCoordinator
{
    Task<PairedDeviceUnpairResult> UnpairAsync(string deviceId);
}
