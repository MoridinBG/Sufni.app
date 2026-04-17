using Sufni.App.Stores;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveDaqSharedStreamRegistry
{
    ILiveDaqSharedStream GetOrCreate(LiveDaqSnapshot snapshot);
}