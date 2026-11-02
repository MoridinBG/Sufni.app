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
using Sufni.App.Video.Services;
using Sufni.App.Video.Views;

namespace Sufni.App.macOS;

internal sealed class MacOsVideoPlayerFactory(IUiThreadDispatcher uiThreadDispatcher, IVideoFileAccess fileAccess) : IVideoPlayerFactory
{
    public IVideoPlayer Create()
    {
        var host = new MacOsAvPlayerHost();
        return new NativeVideoPlayer(new AppleVideoSurface(host, host.SetPlayer, uiThreadDispatcher), fileAccess, uiThreadDispatcher);
    }
}

internal sealed class MacOsAvPlayerHost : NativeControlHost
{
    private AVPlayer? player;
    private AVPlayerView? playerView;

    public void SetPlayer(AVPlayer? value)
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
