using System.Threading.Tasks;

namespace Sufni.App.SyncAndPairing.Coordinators;

public interface IPairedDeviceCoordinator
{
    Task<PairedDeviceUnpairResult> UnpairAsync(string deviceId);
}
