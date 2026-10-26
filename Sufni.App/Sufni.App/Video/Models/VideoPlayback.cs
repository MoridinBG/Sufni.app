using System;
using Avalonia.Controls;

namespace Sufni.App.Video.Models;

/// <summary>
/// Creates native video players. Registered by the platform heads that can
/// play video; its presence matches <c>AppCapabilities.CanPlayVideo</c>.
/// </summary>
public interface IVideoPlayerFactory
{
    IVideoPlayer Create();
}

/// <summary>
/// One native player: the control that hosts its surface and the playback
/// backend that drives it.
/// </summary>
public interface IVideoPlayer : IDisposable
{
    Control View { get; }
    IVideoPlaybackBackend Backend { get; }
    event EventHandler<VideoSizeChangedEventArgs>? VideoSizeChanged;
    void Load(Uri? videoUri);
}

/// <summary>
/// Playback control of a native player. Events are raised on the UI thread.
/// </summary>
public interface IVideoPlaybackBackend
{
    bool IsPlaying { get; }
    event EventHandler? PlaybackStateChanged;
    event EventHandler<VideoPositionChangedEventArgs>? PositionChanged;
    void Play();
    void Pause();
    void Seek(TimeSpan position);
}

public sealed class VideoSizeChangedEventArgs(double width, double height) : EventArgs
{
    public double Width { get; } = width;
    public double Height { get; } = height;
}

public sealed class VideoPositionChangedEventArgs(TimeSpan position) : EventArgs
{
    public TimeSpan Position { get; } = position;
}
