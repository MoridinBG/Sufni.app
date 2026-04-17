namespace Sufni.App.Services.LiveStreaming;

// Creates a fresh transport client for each live preview tab.
public interface ILiveDaqClientFactory
{
    // Returns a new client instance; callers should not share one client across tabs.
    ILiveDaqClient CreateClient();
}