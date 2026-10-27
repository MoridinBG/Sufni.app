using System;
using System.Threading.Tasks;
using AVFoundation;
using AVKit;
using Avalonia.Controls;
using Avalonia.Platform;
using Foundation;
using Sufni.App.Infrastructure;
using Sufni.App.AppleShared;
using Sufni.App.Video.Models;

namespace Sufni.App.macOS;

internal sealed class MacOsVideoPlayerFactory(IUiThreadDispatcher uiThreadDispatcher) : IVideoPlayerFactory
{
    public IVideoPlayer Create() => new MacOsVideoPlayer(uiThreadDispatcher);
}

internal sealed class MacOsVideoPlayer : IVideoPlayer
{
    private readonly AVPlayer player = new();
    private readonly MacOsAvPlayerHost host = new();
    private readonly AppleAvPlaybackBackend backend;
    private AVPlayerItem? currentItem;
    private int loadVersion;

    public MacOsVideoPlayer(IUiThreadDispatcher uiThreadDispatcher)
    {
        backend = new AppleAvPlaybackBackend(player, uiThreadDispatcher);
        host.SetPlayer(player);
    }

    public Control View => host;
    public IVideoPlaybackBackend Backend => backend;
    public event EventHandler<VideoSizeChangedEventArgs>? VideoSizeChanged;

    public void Load(Uri? videoUri)
    {
        var version = ++loadVersion;
        player.Pause();
        var previousItem = currentItem;

        if (videoUri is null)
        {
            player.ReplaceCurrentItemWithPlayerItem(null);
            currentItem = null;
            previousItem?.Dispose();
            return;
        }

        var asset = AVAsset.FromUrl(NSUrl.FromFilename(videoUri.LocalPath));
        var nextItem = new AVPlayerItem(asset);
        currentItem = nextItem;
        player.ReplaceCurrentItemWithPlayerItem(nextItem);
        previousItem?.Dispose();
        _ = UpdateVideoSizeAsync(asset, version);
    }

    private async Task UpdateVideoSizeAsync(AVAsset asset, int version)
    {
        var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video);
        var track = tracks.Length > 0 ? tracks[0] : null;
        if (version != loadVersion || track is null || track.NaturalSize.Width <= 0 || track.NaturalSize.Height <= 0)
        {
            return;
        }

        VideoSizeChanged?.Invoke(
            this,
            new VideoSizeChangedEventArgs(track.NaturalSize.Width, track.NaturalSize.Height));
    }

    public void Dispose()
    {
        player.Pause();
        player.ReplaceCurrentItemWithPlayerItem(null);
        currentItem?.Dispose();
        backend.Dispose();
        player.Dispose();
    }
}

internal sealed class MacOsAvPlayerHost : NativeControlHost
{
    private AVPlayer? player;
    private AVPlayerView? playerView;

    public void SetPlayer(AVPlayer value)
    {
        player = value;
        playerView?.Player = player;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        playerView = new PassiveAvPlayerView
        {
            ControlsStyle = AVPlayerViewControlsStyle.None,
            Player = player,
        };

        return new PlatformHandle(playerView.Handle, "NSView");
    }

    // AVKit's player view accepts first responder and handles Space as its
    // own play/pause shortcut, silently pausing the player without the app
    // ever seeing the key. Embedded as a passive rendering surface it must
    // leave all keyboard input to Avalonia, where Space drives timeline
    // playback.
    private sealed class PassiveAvPlayerView : AVPlayerView
    {
        public override bool AcceptsFirstResponder() => false;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (playerView is not null)
        {
            playerView.Player = null;
            playerView.Dispose();
            playerView = null;
        }

        base.DestroyNativeControlCore(control);
    }
}
