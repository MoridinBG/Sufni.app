using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Shell.Coordinators;

internal interface IStartupJob
{
    Task RunAsync(CancellationToken cancellationToken);
}
