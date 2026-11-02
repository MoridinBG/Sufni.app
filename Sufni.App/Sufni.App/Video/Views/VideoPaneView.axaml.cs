using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Sufni.App.Video.Models;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Video.Views;

// Owns the native player while attached. Detaching pauses playback by
// disposing the player and releases cursor control; reattaching creates a new
// player for the same pane view model.
public sealed partial class VideoPaneView : UserControl
{
    private readonly DispatcherTimer controlsHideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };

    private static readonly ILogger logger = Log.ForContext<VideoPaneView>();
    private readonly CancellableOperation loadOperation = new();
    private VideoPaneViewModel? pane;
    private IVideoPlayer? videoPlayer;
    private bool controlsPopupUnavailable;
    private bool isPointerOverPlayer;
    private bool isPointerOverControls;
    private TopLevel? keyDownTopLevel;

    public VideoPaneView()
    {
        InitializeComponent();
        controlsHideTimer.Tick += OnControlsHideTimerTick;
        PointerEntered += OnPlayerPointerEntered;
        PointerExited += OnPlayerPointerExited;
        PointerPressed += OnPlayerPointerPressed;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachPane(DataContext as VideoPaneViewModel);

        keyDownTopLevel = TopLevel.GetTopLevel(this);
        keyDownTopLevel?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Bubble);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        keyDownTopLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        keyDownTopLevel = null;
        ControlsPopup.IsOpen = false;
        controlsHideTimer.Stop();
        isPointerOverPlayer = false;
        isPointerOverControls = false;
        AttachPane(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
        {
            AttachPane(DataContext as VideoPaneViewModel);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            UpdateVideoFrameSize();
        }
    }

    private void AttachPane(VideoPaneViewModel? value)
    {
        if (ReferenceEquals(pane, value))
        {
            return;
        }

        if (pane is not null)
        {
            pane.PropertyChanged -= OnPanePropertyChanged;
            pane.Playback.PropertyChanged -= OnPlaybackPropertyChanged;
            DisposePlayer(pane);
        }

        pane = value;
        if (pane is null)
        {
            return;
        }

        pane.PropertyChanged += OnPanePropertyChanged;
        pane.Playback.PropertyChanged += OnPlaybackPropertyChanged;
        CreatePlayer(pane);
        UpdateVideoFrameSize();
    }

    private void CreatePlayer(VideoPaneViewModel value)
    {
        try
        {
            videoPlayer = value.PlayerFactory.Create();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Native video player could not be created");
            value.Playback.ReportFailure(new(VideoPlaybackFailureReason.PlayerUnavailable, ex.Message));
            return;
        }

        videoPlayer.VideoSizeChanged += OnVideoSizeChanged;
        videoPlayer.PlaybackFailed += OnPlaybackFailed;
        VideoHost.Content = videoPlayer.View;
        LoadVideo(value.VideoUri);
    }

    private void DisposePlayer(VideoPaneViewModel value)
    {
        loadOperation.Cancel();
        value.Playback.SetBackend(null);
        VideoHost.Content = null;
        if (videoPlayer is not null)
        {
            videoPlayer.VideoSizeChanged -= OnVideoSizeChanged;
            videoPlayer.PlaybackFailed -= OnPlaybackFailed;
            videoPlayer.Dispose();
            videoPlayer = null;
        }
    }

    private void LoadVideo(string? videoUri)
    {
        if (videoPlayer is null || pane is null) return;
        var token = loadOperation.Start();
        pane.Playback.SetVideoSize(16, 9);
        pane.Playback.BeginLoad(videoUri is not null);
        _ = LoadVideoAsync(videoPlayer, pane, videoUri, token);
    }

    private static async Task LoadVideoAsync(IVideoPlayer player, VideoPaneViewModel value, string? uri, CancellationToken token)
    {
        try
        {
            await player.LoadAsync(uri is null ? null : new Uri(uri), token);
            if (!token.IsCancellationRequested) value.Playback.CompleteLoad(uri is null ? null : player.Backend);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested) return;
            logger.Error(exception, "Video load failed for {VideoUri}", uri);
            value.Playback.ReportFailure(new(VideoPlaybackFailureReason.Open, exception.Message));
        }
    }

    private void OnPlaybackFailed(object? sender, VideoPlaybackFailedEventArgs args)
    {
        if (ReferenceEquals(sender, videoPlayer)) pane?.Playback.ReportFailure(args.Failure);
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoPaneViewModel.VideoUri))
        {
            LoadVideo(pane?.VideoUri);
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoPlaybackViewModel.VideoWidth)
            or nameof(VideoPlaybackViewModel.VideoHeight))
        {
            UpdateVideoFrameSize();
        }
    }

    private void OnVideoSizeChanged(object? sender, VideoSizeChangedEventArgs e)
    {
        pane?.Playback.SetVideoSize(e.Width, e.Height);
    }

    private void UpdateVideoFrameSize()
    {
        var availableWidth = Bounds.Width;
        var availableHeight = Bounds.Height;
        if (pane is null || availableWidth <= 0 || availableHeight <= 0)
        {
            VideoFrame.Width = double.NaN;
            VideoFrame.Height = double.NaN;
            return;
        }

        var aspectRatio = pane.Playback.VideoWidth / pane.Playback.VideoHeight;
        var fittedWidth = availableWidth;
        var fittedHeight = fittedWidth / aspectRatio;
        if (fittedHeight > availableHeight)
        {
            fittedHeight = availableHeight;
            fittedWidth = fittedHeight * aspectRatio;
        }

        VideoFrame.Width = fittedWidth;
        VideoFrame.Height = fittedHeight;
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled ||
            args.Key != Key.S ||
            args.KeyModifiers != KeyModifiers.None ||
            IsTextInputSource(args.Source) ||
            !IsVisibleInVisualTree() ||
            pane?.Playback.ToggleSyncCommand.CanExecute(null) != true)
        {
            return;
        }

        pane.Playback.ToggleSyncCommand.Execute(null);
        args.Handled = true;
    }

    private bool IsVisibleInVisualTree()
    {
        for (Visual? visual = this; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { IsVisible: false })
            {
                return false;
            }
        }

        return VisualRoot is not null;
    }

    private static bool IsTextInputSource(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPlayerPointerEntered(object? sender, PointerEventArgs e)
    {
        isPointerOverPlayer = true;
        ShowControls();
    }

    private void OnPlayerPointerExited(object? sender, PointerEventArgs e)
    {
        isPointerOverPlayer = false;
        ScheduleHideControls();
    }

    private void OnPlayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Mouse ||
            IsControlsOverlaySource(e.Source))
        {
            return;
        }

        controlsHideTimer.Stop();
        ControlsPopup.IsOpen = !ControlsPopup.IsOpen;
        e.Handled = true;
    }

    private void ControlsOverlay_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        isPointerOverControls = true;
        ShowControls();
    }

    private void ControlsOverlay_OnPointerExited(object? sender, PointerEventArgs e)
    {
        isPointerOverControls = false;
        ScheduleHideControls();
    }

    private bool IsControlsOverlaySource(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, ControlsOverlay))
            {
                return true;
            }
        }

        return false;
    }

    // The native surface covers in-tree overlays, so the controls live in a
    // popup with its own top level.
    private void ShowControls()
    {
        controlsHideTimer.Stop();
        if (controlsPopupUnavailable || ControlsPopup.IsOpen)
        {
            return;
        }

        try
        {
            ControlsPopup.IsOpen = true;
        }
        catch (InvalidOperationException)
        {
            controlsPopupUnavailable = true;
        }
    }

    private void ScheduleHideControls()
    {
        controlsHideTimer.Stop();
        controlsHideTimer.Start();
    }

    private void OnControlsHideTimerTick(object? sender, EventArgs e)
    {
        controlsHideTimer.Stop();
        if (!isPointerOverPlayer && !isPointerOverControls)
        {
            ControlsPopup.IsOpen = false;
        }
    }
}
