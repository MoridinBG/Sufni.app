using System;
using System.Threading.Tasks;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public interface ITelemetryFile
{
    public string Name { get; set; }
    public string FileName { get; }
    public bool? ShouldBeImported { get; set; }
    public bool Imported { get; set; }
    public string Description { get; set; }
    public DateTime StartTime { get; }
    public string Duration { get; }
    public bool Malformed { get; }

    public Task<byte[]> GeneratePsstAsync(BikeData bikeData);
    public Task OnImported();
    public Task OnTrashed();
}