
using Sufni.App.LiveDaq.Stores;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public interface ILiveDaqSharedStreamRegistry
{
    ILiveDaqSharedStream GetOrCreate(LiveDaqSnapshot snapshot);
}