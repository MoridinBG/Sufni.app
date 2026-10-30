using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
namespace Sufni.App.Acquisition.Coordinators;

public interface IImportSessionsCoordinator
{
    Task OpenAsync();

    SessionImportState State { get; }

    IObservable<SessionImportState> Observe();

    Task ImportAsync(
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId);
}
