using System;
using System.Threading.Tasks;
using AVFoundation;
using Avalonia.Controls;
using Avalonia.Platform;
using Foundation;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.AppleShared;
using Sufni.App.Video.Models;
using UIKit;

namespace Sufni.App.iOS;

internal sealed class IosVideoPlayerFactory(IUiThreadDispatcher uiThreadDispatcher) : IVideoPlayerFactory
{
    public IVideoPlayer Create() => new IosVideoPlayer(uiThreadDispatcher);
}

internal sealed class IosVideoPlayer : IVideoPlayer
{
    private readonly AVPlayer player = new();
    private readonly IosAvPlayerHost host = new();
    private readonly AppleAvPlaybackBackend backend;
    private AVPlayerItem? currentItem;
    private int loadVersion;

    public IosVideoPlayer(IUiThreadDispatcher uiThreadDispatcher)
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
        var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video.GetConstant()!);
        var track = tracks.Count > 0 ? tracks[0] : null;
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

internal sealed class IosAvPlayerHost : NativeControlHost
{
    private AVPlayer? player;
    private PassiveAvPlayerView? playerView;

    public void SetPlayer(AVPlayer value)
    {
        player = value;
        if (playerView is not null)
        {
            playerView.Player = player;
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        playerView = new PassiveAvPlayerView
        {
            Player = player,
        };

        return new PlatformHandle(playerView.Handle, "UIView");
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

    private sealed class PassiveAvPlayerView : UIView
    {
        private readonly AVPlayerLayer playerLayer = new()
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspect,
        };

        public PassiveAvPlayerView()
        {
            BackgroundColor = UIColor.Black;
            UserInteractionEnabled = false;
            Layer.AddSublayer(playerLayer);
        }

        public AVPlayer? Player
        {
            get => playerLayer.Player;
            set => playerLayer.Player = value;
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            playerLayer.Frame = Bounds;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                playerLayer.Player = null;
                playerLayer.RemoveFromSuperLayer();
                playerLayer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
