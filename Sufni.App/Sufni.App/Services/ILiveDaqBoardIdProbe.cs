using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface ILiveDaqBoardIdProbe
{
    Task<System.Guid?> ProbeAsync(IPAddress address, int port, CancellationToken cancellationToken = default);
}