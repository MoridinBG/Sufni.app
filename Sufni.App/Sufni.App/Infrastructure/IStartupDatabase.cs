using System.Threading.Tasks;

namespace Sufni.App.Infrastructure;

public interface IStartupDatabase
{
    Task Initialization { get; }

    // Replaces the files for the next launch; the current graph remains unusable.
    Task<string> ArchiveAndResetAsync();
}
