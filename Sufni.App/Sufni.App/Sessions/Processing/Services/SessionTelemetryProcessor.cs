using System;
using System.IO;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Processing.Services;

internal interface ISessionTelemetryProcessor
{
    TelemetryData ReadProcessedTelemetryData(byte[] processedData);

}

internal sealed class SessionTelemetryProcessor : ISessionTelemetryProcessor
{
    public TelemetryData ReadProcessedTelemetryData(byte[] processedData)
    {
        try
        {
            return TelemetryData.FromBinary(processedData)
                   ?? throw new InvalidDataException("Processed session data did not contain telemetry.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Processed session data is not valid telemetry.", exception);
        }
    }

}
