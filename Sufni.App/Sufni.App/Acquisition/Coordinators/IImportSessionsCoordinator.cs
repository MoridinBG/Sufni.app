using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
namespace Sufni.App.Acquisition.Coordinators;

public interface IImportSessionsCoordinator
{
    SessionImportState State { get; }

    IObservable<SessionImportState> Observe();

    Task ImportAsync(
        ITelemetryDataStore dataStore,
        IReadOnlyList<SessionImportItem> files,
        Guid setupId,
        CancellationToken cancellationToken = default);

    Task StopAsync();
}
