using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Queries;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Services.Management;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Services;

internal sealed class LiveProfilingLaunchService(
    IBikeStore bikeStore,
    ISetupStore setupStore,
    ISessionStore sessionStore,
    ILiveDaqStoreWriter liveDaqStore,
    ILiveDaqSharedStreamRegistry sharedStreamRegistry,
    ILiveSessionServiceFactory liveSessionServiceFactory,
    LiveDaqCoordinator liveDaqCoordinator,
    SessionCoordinator sessionCoordinator,
    ISessionPresentationService sessionPresentationService,
    IBackgroundTaskRunner backgroundTaskRunner,
    ITileLayerService tileLayerService,
    IDaqManagementService daqManagementService,
    IFilesService filesService,
    IShellCoordinator shell,
    IDialogService dialogService,
    ILiveDaqKnownBoardsQuery knownBoardsQuery)
{
    private const string ViewEnvironmentKey = "SUFNI_PROFILE_LIVE_VIEW";
    private const string HostEnvironmentKey = "SUFNI_PROFILE_LIVE_HOST";
    private const string PortEnvironmentKey = "SUFNI_PROFILE_LIVE_PORT";
    private const string BoardIdEnvironmentKey = "SUFNI_PROFILE_LIVE_BOARD_ID";
    private const string SetupIdEnvironmentKey = "SUFNI_PROFILE_LIVE_SETUP_ID";

    private const string DefaultHost = "192.168.4.1";
    private const int DefaultPort = 1557;
    private const uint ProfileTravelHz = 1000;
    private const uint ProfileImuHz = 1000;
    private const uint ProfileGpsHz = 0;

    private static readonly Guid DefaultBoardId = Guid.Parse("00000000-0000-5980-8034-dcc01ee18f70");
    private static readonly Guid DefaultSetupId = Guid.Parse("95d7843e-3633-4d68-9674-213e34fad345");
    private static readonly ILogger logger = Log.ForContext<LiveProfilingLaunchService>();

    public static bool IsEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ViewEnvironmentKey));

    public async Task LaunchFromEnvironmentAsync()
    {
        var options = ReadOptions();
        if (options is null)
        {
            return;
        }

        await bikeStore.RefreshAsync();
        await setupStore.RefreshAsync();
        await sessionStore.RefreshAsync();

        var setup = setupStore.Get(options.SetupId)
            ?? throw new InvalidOperationException($"Setup {options.SetupId} was not found in the app database.");
        var bike = bikeStore.Get(setup.BikeId)
            ?? throw new InvalidOperationException($"Bike {setup.BikeId} for setup {options.SetupId} was not found in the app database.");

        var context = CreateSessionContext(options, setup, bike);
        var snapshot = new LiveDaqSnapshot(
            IdentityKey: options.BoardId.ToString(),
            DisplayName: context.DisplayName,
            BoardId: options.BoardId.ToString(),
            Host: options.Host,
            Port: options.Port,
            IsOnline: true,
            SetupName: setup.Name,
            BikeName: bike.Name);

        liveDaqStore.ReplaceAll([snapshot]);

        var sharedStream = sharedStreamRegistry.GetOrCreate(snapshot);
        var launchCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                switch (options.View)
                {
                    case LiveProfilingView.Details:
                        await LaunchDetailsAsync(snapshot, sharedStream);
                        break;
                    case LiveProfilingView.Session:
                        await LaunchSessionAsync(context, sharedStream);
                        break;
                }

                var state = sharedStream.CurrentState;
                var header = state.SessionHeader;
                var readyMessage = FormattableString.Invariant(
                    $"SUFNI_PROFILE_LIVE_READY {options.ViewName} {snapshot.Endpoint} travel={header?.AcceptedTravelHz ?? 0} imu={header?.AcceptedImuHz ?? 0}");
                Console.WriteLine(readyMessage);
                logger.Information("{ReadyMessage}", readyMessage);
                launchCompletion.SetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SUFNI_PROFILE_LIVE_FAILED {options.ViewName}: {ex.Message}");
                logger.Error(ex, "Live profiling launch failed for {ViewName}", options.ViewName);
                launchCompletion.SetException(ex);
            }
        }, DispatcherPriority.Background);

        await launchCompletion.Task;
    }

    private async Task LaunchDetailsAsync(LiveDaqSnapshot snapshot, ILiveDaqSharedStream sharedStream)
    {
        var editor = new LiveDaqDetailViewModel(
            snapshot,
            sharedStream,
            liveDaqCoordinator,
            daqManagementService,
            filesService,
            shell,
            dialogService,
            knownBoardsQuery,
            liveDaqStore);

        editor.RequestedTravelHz = ProfileTravelHz;
        editor.RequestedImuHz = ProfileImuHz;
        editor.RequestedGpsFixHz = ProfileGpsHz;
        shell.Open(editor);
        await editor.LoadedCommand.ExecuteAsync(null);
        await editor.ConnectCommand.ExecuteAsync(null);
        EnsureConnected(sharedStream, "live details");
    }

    private async Task LaunchSessionAsync(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream)
    {
        await sharedStream.ApplyConfigurationAsync(LiveDaqStreamConfiguration.FromRequestedRates(
            ProfileTravelHz,
            ProfileImuHz,
            ProfileGpsHz));

        var editor = new LiveSessionDetailViewModel(
            context,
            liveSessionServiceFactory.Create(context, sharedStream),
            sessionCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            tileLayerService,
            shell,
            dialogService);

        shell.Open(editor);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 1200, 800));
        EnsureConnected(sharedStream, "live session");
    }

    private static void EnsureConnected(ILiveDaqSharedStream sharedStream, string label)
    {
        var state = sharedStream.CurrentState;
        if (state.ConnectionState == LiveConnectionState.Connected)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Could not start {label}: {state.LastError ?? state.ConnectionState.ToString()}.");
    }

    private static LiveDaqSessionContext CreateSessionContext(
        LiveProfilingLaunchOptions options,
        SetupSnapshot setupSnapshot,
        BikeSnapshot bikeSnapshot)
    {
        var setup = new Setup(setupSnapshot.Id, setupSnapshot.Name)
        {
            BikeId = setupSnapshot.BikeId,
            FrontSensorConfigurationJson = setupSnapshot.FrontSensorConfigurationJson,
            RearSensorConfigurationJson = setupSnapshot.RearSensorConfigurationJson,
            Updated = setupSnapshot.Updated,
        };
        var bike = Bike.FromSnapshot(bikeSnapshot);
        var frontSensorConfiguration = setup.FrontSensorConfiguration(bike);
        RearTravelCalibrationBuilder.TryBuild(setup, bike, out var rearTravelCalibration, out _);
        var calibration = CreateTravelCalibration(frontSensorConfiguration, rearTravelCalibration);

        return new LiveDaqSessionContext(
            IdentityKey: options.BoardId.ToString(),
            BoardId: options.BoardId,
            DisplayName: setupSnapshot.Name,
            SetupId: setupSnapshot.Id,
            SetupName: setupSnapshot.Name,
            BikeId: bikeSnapshot.Id,
            BikeName: bikeSnapshot.Name,
            BikeData: TelemetryBikeData.Create(bike, frontSensorConfiguration, rearTravelCalibration),
            TravelCalibration: calibration);
    }

    private static LiveDaqTravelCalibration CreateTravelCalibration(
        ISensorConfiguration? frontSensorConfiguration,
        RearTravelCalibration? rearTravelCalibration) => new(
            CreateCalibration(frontSensorConfiguration),
            CreateCalibration(rearTravelCalibration));

    private static LiveDaqTravelChannelCalibration? CreateCalibration(ISensorConfiguration? configuration)
    {
        if (configuration is null || configuration.MaxTravel <= 0)
        {
            return null;
        }

        return new LiveDaqTravelChannelCalibration(configuration.MaxTravel, configuration.MeasurementToTravel);
    }

    private static LiveDaqTravelChannelCalibration? CreateCalibration(RearTravelCalibration? calibration)
    {
        if (calibration is null || calibration.MaxTravel <= 0)
        {
            return null;
        }

        return new LiveDaqTravelChannelCalibration(calibration.MaxTravel, calibration.MeasurementToTravel);
    }

    private static LiveProfilingLaunchOptions? ReadOptions()
    {
        var viewName = Environment.GetEnvironmentVariable(ViewEnvironmentKey);
        if (string.IsNullOrWhiteSpace(viewName))
        {
            return null;
        }

        var view = viewName.Trim().ToLowerInvariant() switch
        {
            "details" or "detail" => LiveProfilingView.Details,
            "session" or "live-session" => LiveProfilingView.Session,
            _ => throw new InvalidOperationException(
                $"{ViewEnvironmentKey} must be 'details' or 'session'.")
        };

        return new LiveProfilingLaunchOptions(
            View: view,
            ViewName: viewName.Trim(),
            Host: ReadString(HostEnvironmentKey, DefaultHost),
            Port: ReadInt(PortEnvironmentKey, DefaultPort),
            BoardId: ReadGuid(BoardIdEnvironmentKey, DefaultBoardId),
            SetupId: ReadGuid(SetupIdEnvironmentKey, DefaultSetupId));
    }

    private static string ReadString(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int ReadInt(string key, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"{key} must be a positive integer.");
    }

    private static Guid ReadGuid(string key, Guid fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{key} must be a GUID.");
    }

    private enum LiveProfilingView
    {
        Details,
        Session,
    }

    private sealed record LiveProfilingLaunchOptions(
        LiveProfilingView View,
        string ViewName,
        string Host,
        int Port,
        Guid BoardId,
        Guid SetupId);
}