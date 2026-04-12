using System.Threading.Tasks;

namespace Sufni.App.Coordinators;

// Owns live DAQ list activation, discovery reconciliation, and detail-tab routing.
public interface ILiveDaqCoordinator
{
    // Starts page-scoped discovery and seeds the runtime list.
    void Activate();

    // Releases page-scoped discovery and collapses the runtime list back to known
    // offline boards.
    void Deactivate();

    // Opens or focuses the detail tab for the given live DAQ identity.
    Task SelectAsync(string identityKey);
}