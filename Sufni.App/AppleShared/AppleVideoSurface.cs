using System;
using System.IO;
using System.Threading.Tasks;
using AVFoundation;
using Avalonia.Controls;
using Foundation;
using Sufni.App.Infrastructure;
using Sufni.App.Video.Models;

namespace Sufni.App.AppleShared;

internal sealed class AppleVideoSurface : IVideoPlayerSurface
{
    private readonly AVPlayer player = new();
    private readonly AppleAvPlaybackBackend backend;
    private readonly Action<AVPlayer?> attachPlayer;
    private readonly IUiThreadDispatcher dispatcher;
    private AVPlayerItem? currentItem;
    private IDisposable? statusObserver;

    public AppleVideoSurface(Control view, Action<AVPlayer?> attachPlayer, IUiThreadDispatcher dispatcher)
    {
        View = view;
        this.attachPlayer = attachPlayer;
        this.dispatcher = dispatcher;
        backend = new AppleAvPlaybackBackend(player, dispatcher);
        attachPlayer(player);
    }

    public Control View { get; }
    public IVideoPlaybackBackend Backend => backend;
    public event EventHandler<NativeVideoFailureEventArgs>? Failed;
    public event EventHandler<NativeVideoSizeEventArgs>? SizeChanged;

    public async Task LoadAsync(NativeVideoSource source)
    {
        using var url = NSUrl.FromFilename(source.Uri.LocalPath);
        using var asset = AVAsset.FromUrl(url);
        var item = new AVPlayerItem(asset);
        currentItem = item;
        statusObserver = item.AddObserver("status", NSKeyValueObservingOptions.New | NSKeyValueObservingOptions.Initial,
            _ => dispatcher.Post(() =>
            {
                if (source.CancellationToken.IsCancellationRequested) return;
                if (item.Status == AVPlayerItemStatus.Failed)
                    Failed?.Invoke(this, new(source, item.Error is { } error
                        ? new NSErrorException(error) : new InvalidOperationException("AVFoundation reported a playback failure.")));
            }));
        player.ReplaceCurrentItemWithPlayerItem(item);
        var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video);
        try
        {
            source.CancellationToken.ThrowIfCancellationRequested();
            var track = tracks.Length > 0 ? tracks[0] : null;
            if (track is null || track.NaturalSize.Width <= 0 || track.NaturalSize.Height <= 0)
                throw new InvalidDataException("The file contains no playable video track.");
            SizeChanged?.Invoke(this, new(source, track.NaturalSize.Width, track.NaturalSize.Height));
        }
        finally
        {
            foreach (var track in tracks) track.Dispose();
        }
    }

    public void Clear()
    {
        statusObserver?.Dispose();
        statusObserver = null;
        player.Pause();
        player.ReplaceCurrentItemWithPlayerItem(null);
        currentItem?.Dispose();
        currentItem = null;
    }

    public void Dispose()
    {
        Clear();
        attachPlayer(null);
        backend.Dispose();
        player.Dispose();
    }
}
