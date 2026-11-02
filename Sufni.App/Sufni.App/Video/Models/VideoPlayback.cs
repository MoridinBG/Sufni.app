using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Shared.Models;
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
    event EventHandler<VideoPlaybackFailedEventArgs>? PlaybackFailed;
    Task LoadAsync(Uri? videoUri, CancellationToken cancellationToken = default);
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

public sealed record VideoPlaybackFailure(VideoPlaybackFailureReason Reason, string? Details = null);

public sealed class VideoPlaybackFailedEventArgs(VideoPlaybackFailure failure) : EventArgs
{
    public VideoPlaybackFailure Failure { get; } = failure;
}

public abstract record VideoPlaybackSourceState
{
    private VideoPlaybackSourceState() { }
    public sealed record Loading : VideoPlaybackSourceState;
    public sealed record Ready : VideoPlaybackSourceState;
    public sealed record Failed(VideoPlaybackFailure Failure) : VideoPlaybackSourceState;
}

/// <summary>Immutable input scoped to one native source load.</summary>
public sealed record NativeVideoSource(Uri Uri, CancellationToken CancellationToken);

/// <summary>Native framework edge. The shared player owns load, access and failure lifetimes.</summary>
public interface IVideoPlayerSurface : IDisposable
{
    Control View { get; }
    IVideoPlaybackBackend Backend { get; }
    event EventHandler<NativeVideoFailureEventArgs>? Failed;
    event EventHandler<NativeVideoSizeEventArgs>? SizeChanged;
    void Clear();
    Task LoadAsync(NativeVideoSource source);
}

public sealed class NativeVideoFailureEventArgs(NativeVideoSource source, Exception exception) : EventArgs
{
    public NativeVideoSource Source { get; } = source;
    public Exception Exception { get; } = exception;
}

public sealed class NativeVideoSizeEventArgs(NativeVideoSource source, double width, double height) : EventArgs
{
    public NativeVideoSource Source { get; } = source;
    public double Width { get; } = width;
    public double Height { get; } = height;
}
