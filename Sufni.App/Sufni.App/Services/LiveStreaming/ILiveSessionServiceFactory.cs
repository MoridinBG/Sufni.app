using Sufni.App.Queries;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveSessionServiceFactory
{
    ILiveSessionService Create(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream);
    ILiveSessionService Create(LiveDaqSessionContext context, ILiveDaqSharedStreamReservation sharedStreamReservation);
}