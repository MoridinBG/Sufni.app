using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Stores;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Builds the canonical hash for setup and bike inputs that affect telemetry
/// processing. The payload is normalized to include calibration and suspension
/// geometry while ignoring metadata that does not change computed telemetry.
/// </summary>
public static class ProcessingDependencyHash
{
    public static string Compute(SetupSnapshot setup, BikeSnapshot bike)
    {
        return Compute(ProcessingDependencyInputs.Create(setup, bike), AppJson.Options);
    }

    internal static string Compute(SetupProcessingInput setup, BikeProcessingInput bike)
    {
        return Compute(ProcessingDependencyInputs.Create(setup, bike), AppJson.Options);
    }

    internal static string Compute(ProcessingDependencyInputs inputs)
    {
        return Compute(inputs, AppJson.Options);
    }

    private static string Compute(ProcessingDependencyInputs inputs, JsonSerializerOptions jsonOptions)
    {
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, inputs, jsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
