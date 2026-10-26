using System;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.Video.Coordinators;
using Sufni.App.Video.Models;
using Sufni.App.Video.Stores;
namespace Sufni.App.Video.ViewModels;

/// <summary>
/// Creates the video pane of a session editor. Returns null on devices that
/// cannot play video.
/// </summary>
public interface IVideoPaneFactory
{
    VideoPaneViewModel? Create(Guid sessionId, IRecordedSessionTimeline timeline, IObservable<VideoSessionTiming> sessionTiming);
}

internal sealed class VideoPaneFactory(
    ISessionVideoStore videoStore,
    IVideoCoordinator videoCoordinator,
    IVideoPlayerFactory? playerFactory = null) : IVideoPaneFactory
{
    public VideoPaneViewModel? Create(Guid sessionId, IRecordedSessionTimeline timeline, IObservable<VideoSessionTiming> sessionTiming) =>
        playerFactory is null
            ? null
            : new VideoPaneViewModel(sessionId, timeline, sessionTiming, videoStore, videoCoordinator, playerFactory);
}
