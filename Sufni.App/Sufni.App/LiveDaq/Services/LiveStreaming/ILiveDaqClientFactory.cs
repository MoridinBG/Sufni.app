using Sufni.App.LiveDaq.Stores;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public interface ILiveDaqClientFactory
{
    ILiveDaqClient Create(LiveDaqSnapshot snapshot);
}
