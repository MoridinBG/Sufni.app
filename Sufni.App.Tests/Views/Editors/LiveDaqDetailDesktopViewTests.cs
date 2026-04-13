using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class LiveDaqDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_RequestRateInputs_DisplayBoundValues()
    {
        var editor = CreateEditor();
        editor.RequestedTravelHz = 200;
        editor.RequestedImuHz = 100;
        editor.RequestedGpsFixHz = 5;

        await using var mounted = await MountAsync(editor);

        Assert.Equal(200, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedTravelHzUpDown")!.Value));
        Assert.Equal(100, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedImuHzUpDown")!.Value));
        Assert.Equal(5, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedGpsFixHzUpDown")!.Value));
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_StatusTexts_DisplayConnectionAndErrorState()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Disconnected, "Busy");
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("Disconnected", mounted.View.FindControl<TextBlock>("ConnectionStateTextBlock")!.Text);
        var lastError = mounted.View.FindControl<TextBlock>("LastErrorTextBlock");
        Assert.NotNull(lastError);
        Assert.Equal("Busy", lastError!.Text);
        Assert.True(lastError.IsVisible);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_AcceptedSessionTexts_DisplayBoundRates()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Connected, null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("Travel: 200 Hz", mounted.View.FindControl<TextBlock>("AcceptedTravelRateTextBlock")!.Text);
        Assert.Equal("IMU: 100 Hz", mounted.View.FindControl<TextBlock>("AcceptedImuRateTextBlock")!.Text);
        Assert.Equal("GPS: 10 Hz", mounted.View.FindControl<TextBlock>("AcceptedGpsRateTextBlock")!.Text);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_TravelTexts_DisplayFormattedValues()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Connected, null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("Front: 112", mounted.View.FindControl<TextBlock>("FrontTravelTextBlock")!.Text);
        Assert.Equal("Rear: 205", mounted.View.FindControl<TextBlock>("RearTravelTextBlock")!.Text);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_ImuAndGpsSections_DisplaySnapshotContent()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Connected, null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("IMUs: 2", mounted.View.FindControl<TextBlock>("ImuSummaryTextBlock")!.Text);
        Assert.Equal("3D fix", mounted.View.FindControl<TextBlock>("GpsStatusTextBlock")!.Text);
        Assert.Equal($"Lat: {48.2082:F4}", mounted.View.FindControl<TextBlock>("GpsCoordinatesTextBlock")!.Text);
        Assert.False(mounted.View.FindControl<TextBlock>("ImuEmptyStateTextBlock")!.IsVisible);
    }

    private static LiveDaqDetailViewModel CreateEditor()
    {
        return new LiveDaqDetailViewModel
        {
            Name = "Board 1",
            Snapshot = LiveDaqUiSnapshot.Empty
        };
    }

    private static async Task<MountedLiveDaqDetailDesktopView> MountAsync(LiveDaqDetailViewModel editor)
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        var view = new LiveDaqDetailDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedLiveDaqDetailDesktopView(host, view);
    }

    private static LiveDaqUiSnapshot CreateSnapshot(LiveConnectionState connectionState, string? error)
    {
        return new LiveDaqUiSnapshot(
            ConnectionState: connectionState,
            ConnectionStateText: LiveDaqUiSnapshot.ToConnectionStateText(connectionState),
            LastError: error,
            LastFrameReceivedUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Session: new LiveSessionContractSnapshot(
                SessionId: 42,
                SelectedSensorMask: LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps,
                AcceptedTravelHz: 200,
                AcceptedImuHz: 100,
                AcceptedGpsFixHz: 10,
                SessionStartUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Flags: LiveSessionFlags.CalibratedOnly,
                ActiveImuLocations: [LiveImuLocation.Frame, LiveImuLocation.Rear]),
            Travel: new LiveTravelUiSnapshot(true, true, 112, 205, TimeSpan.FromSeconds(1.25), 3, 1),
            Imus:
            [
                new LiveImuUiSnapshot(LiveImuLocation.Frame, true, 10, 11, 12, 13, 14, 15, TimeSpan.FromSeconds(1.25), 4, 2),
                new LiveImuUiSnapshot(LiveImuLocation.Rear, true, 20, 21, 22, 23, 24, 25, TimeSpan.FromSeconds(1.25), 4, 2)
            ],
            Gps: new LiveGpsUiSnapshot(
                IsActive: true,
                HasData: true,
                PreviewState: new GpsPreviewState(true, true, "3D fix"),
                FixTimestampUtc: new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                Latitude: 48.2082,
                Longitude: 16.3738,
                Altitude: 180.5f,
                Speed: 7.5f,
                Heading: 125.5f,
                Satellites: 9,
                Epe2d: 1.2f,
                Epe3d: 2.3f,
                QueueDepth: 1,
                DroppedBatches: 0));
    }
}

internal sealed class MountedLiveDaqDetailDesktopView(Window host, LiveDaqDetailDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqDetailDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}