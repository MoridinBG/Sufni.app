using System.Collections.Immutable;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Sessions.Models;
using System;
using System.Threading;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Store;
using System.Threading.Tasks;

namespace Sufni.App.LiveDaq.Coordinators;

public interface ILiveDaqCoordinator
{
    Task<LiveSessionSaveResult> SaveCaptureAsync(SessionSnapshot draft, ILiveSessionService captureService,
        SessionPreferences preferences, CancellationToken cancellationToken = default);

    IObservable<LiveDaqManagementAvailability> ObserveManagementAvailability(string identityKey);
    Task<LiveDaqManagementResult> SetTimeAsync(string identityKey, CancellationToken cancellationToken = default);
    Task<LiveDaqManagementResult> ReadConfigAsync(string identityKey, CancellationToken cancellationToken = default);
    Task<LiveDaqManagementResult> WriteConfigAsync(string identityKey, ReadOnlyMemory<byte> config, CancellationToken cancellationToken = default);
    Task<LiveDaqManagementResult> SaveEditedConfigAsync(string identityKey, DaqConfigDocument document, ImmutableDictionary<string, string> values, CancellationToken cancellationToken = default);
    Task<LiveDaqManagementResult> SelectConfigFileAsync(CancellationToken cancellationToken = default);

    void Activate();

    void Deactivate();

}
