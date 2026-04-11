namespace Sufni.App.Services.LiveStreaming;

public interface ILiveDaqClientFactory
{
    ILiveDaqClient CreateClient();
}