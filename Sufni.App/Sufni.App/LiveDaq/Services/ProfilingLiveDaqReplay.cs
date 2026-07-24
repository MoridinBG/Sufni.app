#if SUFNI_PROFILING_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Shared.Common;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.Kinematics;
using Sufni.Profiling;

namespace Sufni.App.LiveDaq.Services;

internal static class ProfilingLiveDaqReplay
{
    internal const string Corpus = "LIVE-LONG";
    internal const string DisplayName = "SST v5 Replay";
    internal const string Host = "127.0.0.1";
    internal const int Port = 1557;
    internal const string UniqueBoardIdHex = "5934dcc01ee18f70";
    internal const int TargetSeconds = 120;
    internal const long TargetTravelSamples = 120_000;
    internal const long TargetImuSamplesPerLocation = 119_950;
    internal const long TargetGpsSamples = 1_200;
    internal const long TargetTemperatureSamples = 12;
    internal const long TargetMarkers = 2;

    private static readonly Guid BikeId = Guid.Parse("b0010000-0000-4000-8000-000000000001");
    private static readonly Guid SetupId = Guid.Parse("b0010000-0000-4000-8000-000000000002");

    internal static bool IsActive =>
        ProfilingRuntime.IsEnabled &&
        string.Equals(ProfilingRuntime.Options.Corpus, Corpus, StringComparison.Ordinal) &&
        ProfilingRuntime.Options.AppDataPath is not null;

    internal static readonly string BoardId =
        UuidUtil.CreateDeviceUuid(UniqueBoardIdHex).ToString();

    internal static readonly LiveDaqCatalogEntry CatalogEntry = new(
        IdentityKey: BoardId,
        DisplayName: DisplayName,
        BoardId: BoardId,
        Host: Host,
        Port: Port,
        ProtocolVersion: LiveProtocolVersion.V3);

    internal static readonly LiveDaqStreamConfiguration Configuration = new(
        RequestedSensorMask: LiveSensorInstanceMask.None,
        TravelRateMhz: 0,
        ImuRateMhz: 0,
        GpsRateMhz: 0,
        RequestedStreamMask:
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps |
            LiveStreamMask.Battery |
            LiveStreamMask.Marker,
        TemperatureRateMhz: 0,
        RequestGpsDiagnostics: true);

    internal static bool Matches(string identityKey) =>
        string.Equals(identityKey, BoardId, StringComparison.OrdinalIgnoreCase);

    internal static LiveV3StartRequest CreateStartRequest(
        LiveV3Capabilities capabilities) => new()
    {
        StreamRequests = capabilities.Streams
            .Select(capability => new LiveV3StreamRequestRecord(
                StreamKind(capability.Stream),
                RecordFlags: 0,
                capability.SupportedSourceMask,
                capability.SupportedExtensionMask,
                RateMhz: 0,
                BatchDurationMs: 0))
            .ToArray(),
    };

    internal static async Task SeedIfRequestedAsync(IServiceProvider services)
    {
        if (!IsActive)
        {
            return;
        }

        var bikes = services.GetRequiredService<ISynchronizableRepository<Bike>>();
        var setups = services.GetRequiredService<ISynchronizableRepository<Setup>>();
        var boards = services.GetRequiredService<ISynchronizableRepository<Board>>();
        var sessions = services.GetRequiredService<ISynchronizableRepository<Session>>();
        if ((await sessions.GetAllAsync()).Count != 0 ||
            (await bikes.GetAllAsync()).Count != 0 ||
            (await setups.GetAllAsync()).Count != 0 ||
            (await boards.GetAllAsync()).Count != 0)
        {
            throw new InvalidOperationException(
                "BENCH-01 requires isolated app data with no sessions, bikes, setups, or boards.");
        }

        var leverageRatio = LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(0, 0),
            new LeverageRatioPoint(160, 160),
        ]);
        var bike = new Bike(BikeId, "BENCH-01 canonical bike")
        {
            HeadAngle = 90,
            ForkStroke = 160,
            ShockStroke = 160,
            RearSuspension = new RearSuspensionSpec.LeverageRatio(leverageRatio),
        };
        var setup = new Setup(SetupId, "BENCH-01 canonical setup")
        {
            BikeId = bike.Id,
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(
                new LinearForkSensorConfiguration
                {
                    Length = 160,
                    Resolution = 12,
                }),
            RearSensorConfigurationJson = SensorConfiguration.ToJson(
                new LinearShockSensorConfiguration
                {
                    Length = 160,
                    Resolution = 12,
                    Type = SensorType.LinearShockStroke,
                }),
        };
        var board = new Board(UuidUtil.CreateDeviceUuid(UniqueBoardIdHex), setup.Id);

        await bikes.PutAsync(bike);
        await setups.PutAsync(setup);
        await boards.PutAsync(board);
        ProfilingRuntime.Marker(
            "BENCH-01",
            "CanonicalLiveState.Seeded",
            value: $"databasePath={AppPaths.DatabasePath};bikeId={bike.Id};setupId={setup.Id};boardId={board.Id}");
        ProfilingRuntime.Flush();
    }

    private static byte StreamKind(LiveStreamMask stream) => stream switch
    {
        LiveStreamMask.Travel => 1,
        LiveStreamMask.Imu => 2,
        LiveStreamMask.Temperature => 3,
        LiveStreamMask.Gps => 4,
        LiveStreamMask.Battery => 5,
        LiveStreamMask.Marker => 6,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stream),
            stream,
            "Unknown LIVE stream capability."),
    };
}

internal sealed class ProfilingLiveDaqCatalogService(
    ILiveDaqCatalogService inner) : ILiveDaqCatalogService
{
    public IDisposable AcquireBrowse() => inner.AcquireBrowse();

    public IObservable<IReadOnlyList<LiveDaqCatalogEntry>> Observe() =>
        inner.Observe().Select(entries => ProfilingLiveDaqReplay.IsActive
            ? entries
                .Where(entry => !ProfilingLiveDaqReplay.Matches(entry.IdentityKey))
                .Append(ProfilingLiveDaqReplay.CatalogEntry)
                .OrderBy(
                    entry => entry.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
            : entries);
}
#endif
