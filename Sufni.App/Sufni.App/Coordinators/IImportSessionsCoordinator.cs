using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Coordinators;

public interface IImportSessionsCoordinator
{
    Task OpenAsync();

    Task<SessionImportResult> ImportAsync(
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId,
        IProgress<SessionImportEvent>? progress = null);
}
