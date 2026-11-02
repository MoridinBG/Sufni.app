using System;
using System.Threading.Tasks;

using Sufni.App.LiveDaq.Stores;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public interface ILiveDaqSharedStreamRegistry
{
    ILiveDaqSharedStream GetOrCreate(LiveDaqSnapshot snapshot);
    IObservable<DaqTransportHolder?> ObserveTransport(string identityKey);
    Task<DaqTransportAcquisition> AcquireManagementAsync(string identityKey, DaqManagementOperation operation);
    Task<DaqTransportAcquisition> AcquireImportAsync(string identityKey, Guid batchId);
    Task<DaqTransportAcquisition> AcquireIdentifyAsync(string identityKey);
}
