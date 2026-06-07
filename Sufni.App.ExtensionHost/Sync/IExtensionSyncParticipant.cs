using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Sync;

public interface IExtensionSyncParticipant
{
    string ExtensionId { get; }
    Task<ExtensionSyncEnvelope?> CreateBatchAsync(long since, CancellationToken cancellationToken);
    Task<ExtensionSyncApplyResult> ApplyBatchAsync(ExtensionSyncEnvelope envelope, CancellationToken cancellationToken);
}

