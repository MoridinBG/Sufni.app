using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Acquisition.Models;

namespace Sufni.App.Acquisition.Services;

/// <summary>Discovers mounted DAQ drives and accesses their recording layout.</summary>
public interface IDaqDriveProbe
{
    IReadOnlyList<DaqDrive> FindDrives();
    Task<Guid> InitializeAsync(DaqDrive drive, CancellationToken cancellationToken);
    IReadOnlyList<ITelemetryFile> GetFiles(DaqDrive drive);
}
