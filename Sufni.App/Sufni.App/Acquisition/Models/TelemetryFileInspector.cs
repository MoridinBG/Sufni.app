using System;
using System.IO;
using Serilog;
using Sufni.Telemetry;

namespace Sufni.App.Acquisition.Models;

internal static class TelemetryFileInspector
{
    private static readonly ILogger logger = Log.ForContext(typeof(TelemetryFileInspector));

    public static SstFileInspection Inspect(Stream stream, string fileName)
    {
        try
        {
            return RawTelemetryData.InspectStream(stream);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException or EndOfStreamException)
        {
            logger.Warning(exception, "Invalid SST recording {FileName}", fileName);
            return new MalformedSstFileInspection(null, null, null, null,
                "The recording is malformed or uses an unsupported SST format.");
        }
    }
}
