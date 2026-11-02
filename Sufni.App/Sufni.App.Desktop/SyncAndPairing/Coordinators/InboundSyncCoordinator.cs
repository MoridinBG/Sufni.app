using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;

namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>Owns inbound pairing and synchronization writes through their committed publication.</summary>
public sealed class InboundSyncCoordinator(
    ISyncDataStore syncDataStore,
    ISynchronizationApplier rowApplier,
    ISessionSyncApplier sessionSyncApplier,
    IRecordedSessionSourceRepository sourceRepository,
    IRecordedSessionSourceStoreWriter sourceStore,
    IPairedDeviceRepository pairedDeviceRepository,
    IPairedDeviceStoreWriter pairedDeviceStore,
    ISynchronizationTokenService tokens,
    TimeProvider clock,
    IBackgroundTaskRunner backgroundTaskRunner,
    IUiThreadDispatcher uiThreadDispatcher) : IInboundSyncCoordinator, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<InboundSyncCoordinator>();
    private readonly SemaphoreSlim applyGate = new(1, 1);
    private readonly Dictionary<string, PairingChallenge> challenges = [];
    private readonly BehaviorSubject<PairingChallenge?> pairingPrompt = new(null);

    public IObservable<PairingChallenge?> PairingPrompt => pairingPrompt.AsObservable();

    public async Task<PairingCommandResult> RequestPairingAsync(PairingRequest request)
    {
        await applyGate.WaitAsync();
        try
        {
            var now = clock.GetUtcNow();
            foreach (var expired in challenges.Values.Where(value => value.ExpiresAt <= now).ToArray()) challenges.Remove(expired.Pin);
            var challenge = new PairingChallenge(request.DeviceId, PairedDevice.NormalizeDisplayName(request.DisplayName),
                RandomNumberGenerator.GetInt32(100000, 999999).ToString(), now.AddSeconds(SynchronizationProtocol.PinTtlSeconds));
            challenges[challenge.Pin] = challenge;
            await uiThreadDispatcher.InvokeAsync(() => pairingPrompt.OnNext(challenge));
            return PairingCommandResult.Applied;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to start pairing for {DeviceId}", request.DeviceId);
            return PairingCommandResult.Failed;
        }
        finally { applyGate.Release(); }
    }

    public async Task<PairingTokenResult> ConfirmPairingAsync(PairingConfirm request)
    {
        await applyGate.WaitAsync();
        try
        {
            if (!challenges.Remove(request.Pin, out var challenge) || challenge.DeviceId != request.DeviceId ||
                challenge.DisplayName != PairedDevice.NormalizeDisplayName(request.DisplayName) || challenge.ExpiresAt <= clock.GetUtcNow())
                return new PairingTokenResult.Rejected();
            var (snapshot, response) = await backgroundTaskRunner.RunAsync(async () =>
            {
                var access = await tokens.CreateAccessTokenAsync(request.DeviceId);
                var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                var device = new PairedDevice(request.DeviceId, challenge.DisplayName, clock.GetUtcNow().UtcDateTime.AddDays(30))
                    { RefreshTokenHash = RefreshTokenHash.Compute(refresh) };
                await pairedDeviceRepository.PutPairedDeviceAsync(device);
                return (PairedDeviceSnapshot.From(device), new TokenResponse(access, refresh, await syncDataStore.GetDatabaseIncarnationAsync()));
            });
            await uiThreadDispatcher.InvokeAsync(async () =>
            {
                await pairedDeviceStore.PublishPairedDevicesChangedAsync([snapshot]);
                pairingPrompt.OnNext(null);
            });
            return new PairingTokenResult.Issued(response);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to confirm pairing for {DeviceId}", request.DeviceId);
            return new PairingTokenResult.Failed();
        }
        finally { applyGate.Release(); }
    }

    public async Task<PairingTokenResult> RefreshPairingAsync(RefreshRequest request)
    {
        await applyGate.WaitAsync();
        try
        {
            return await backgroundTaskRunner.RunAsync<PairingTokenResult>(async () =>
            {
                var hash = RefreshTokenHash.Compute(request.RefreshToken);
                var current = await pairedDeviceRepository.GetPairedDeviceByTokenHashAsync(hash);
                var now = clock.GetUtcNow().UtcDateTime;
                if (current is null || current.Expires <= now) return new PairingTokenResult.Rejected();
                var access = await tokens.CreateAccessTokenAsync(current.DeviceId);
                var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                var device = await pairedDeviceRepository.RotateRefreshTokenAsync(current.DeviceId, hash,
                    RefreshTokenHash.Compute(refresh), now, now.AddDays(30));
                if (device is null) return new PairingTokenResult.Rejected();
                await pairedDeviceStore.PublishPairedDevicesChangedAsync([PairedDeviceSnapshot.From(device)]);
                return new PairingTokenResult.Issued(new TokenResponse(access, refresh, await syncDataStore.GetDatabaseIncarnationAsync()));
            });
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to refresh pairing credentials");
            return new PairingTokenResult.Failed();
        }
        finally { applyGate.Release(); }
    }

    public async Task<PairingCommandResult> UnpairAsync(UnpairRequest request)
    {
        await applyGate.WaitAsync();
        try
        {
            return await backgroundTaskRunner.RunAsync(async () =>
            {
                var device = await pairedDeviceRepository.GetPairedDeviceAsync(request.DeviceId);
                if (device is not null)
                {
                    var hash = RefreshTokenHash.Compute(request.RefreshToken);
                    if (device.RefreshTokenHash != hash && device.PreviousRefreshTokenHash != hash) return PairingCommandResult.Rejected;
                    await pairedDeviceRepository.DeletePairedDeviceAsync(request.DeviceId);
                }
                await pairedDeviceStore.PublishPairedDevicesRemovedAsync([request.DeviceId]);
                return PairingCommandResult.Applied;
            });
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to unpair {DeviceId}", request.DeviceId);
            return PairingCommandResult.Failed;
        }
        finally { applyGate.Release(); }
    }

    public void Dispose()
    {
        pairingPrompt.Dispose();
        applyGate.Dispose();
    }

    public async Task<SessionDataApplyResult> ApplySessionBlobAsync(Guid id, SessionBlobPayload payload)
    {
        await applyGate.WaitAsync();
        try { return await sessionSyncApplier.ApplyUploadedAsync(id, payload); }
        finally { applyGate.Release(); }
    }

    public async Task<InboundSourceApplyResult> ApplySourceAsync(RecordedSessionSourcePayload payload)
    {
        await applyGate.WaitAsync();
        try
        {
            return await backgroundTaskRunner.RunAsync(async () =>
            {
                try
                {
                    if (!RecordedSessionSourceHash.Matches(payload))
                    {
                        logger.Warning("Recorded source hash does not match its payload for {SessionId}", payload.SessionId);
                        return InboundSourceApplyResult.InvalidPayload;
                    }
                }
                catch (Exception exception) when (exception is System.Text.Json.JsonException or ZstdSharp.ZstdException)
                {
                    logger.Warning(exception, "Recorded source payload is invalid for {SessionId}", payload.SessionId);
                    return InboundSourceApplyResult.InvalidPayload;
                }
                var committed = await sourceRepository.PutRecordedSessionSourceAsync(new RecordedSessionSource
                {
                    SessionId = payload.SessionId, SourceKind = payload.SourceKind, SourceName = payload.SourceName,
                    SchemaVersion = payload.SchemaVersion, SourceHash = payload.SourceHash, Payload = payload.Payload
                });
                await sourceStore.PublishSourcesChangedAsync([committed]);
                return InboundSourceApplyResult.Applied;
            });
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound recorded source for {SessionId}", payload.SessionId);
            return InboundSourceApplyResult.Failed;
        }
        finally { applyGate.Release(); }
    }

    public Task<SynchronizationData> GetPullAsync(long sinceExclusive) => backgroundTaskRunner.RunAsync(async () =>
        await syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Pull, sinceExclusive,
            await syncDataStore.GetSequenceUpperBoundAsync(SynchronizationDirection.Pull)));

    public async Task<InboundSyncWriteResult> ApplyPushAsync(SynchronizationData data)
    {
        await applyGate.WaitAsync();
        try
        {
            await rowApplier.ApplyAsync(data, SynchronizationDirection.Push);
            return InboundSyncWriteResult.Applied;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound synchronization rows");
            return InboundSyncWriteResult.Failed;
        }
        finally { applyGate.Release(); }
    }

}
