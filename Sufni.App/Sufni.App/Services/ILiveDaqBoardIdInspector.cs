using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services;

// Resolves a discovered live DAQ endpoint to a persisted board identity when the
// endpoint exposes BOARDID through the legacy file protocol.
public interface ILiveDaqBoardIdInspector
{
    // `address` and `port` identify the DAQ endpoint to inspect. Returns null when an
    // inspection succeeds but no board identity can be resolved.
    Task<System.Guid?> InspectAsync(IPAddress address, int port, CancellationToken cancellationToken = default);
}