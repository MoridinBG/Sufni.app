using System.Threading.Tasks;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>Applies incoming row facts and publishes one coherent store update.</summary>
public interface ISynchronizationApplier
{
    Task<SynchronizationApplyResult> ApplyAsync(SynchronizationData data, SynchronizationDirection direction);
}
