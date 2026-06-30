
using Sufni.App.LiveDaq.Queries;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public interface ILiveSessionServiceFactory
{
    ILiveSessionService Create(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream);
}