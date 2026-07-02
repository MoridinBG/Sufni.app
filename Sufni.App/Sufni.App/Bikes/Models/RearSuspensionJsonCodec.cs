using System;
using System.Text.Json;
using Sufni.App.Infrastructure;

namespace Sufni.App.Bikes.Models;

internal static class RearSuspensionJsonCodec
{
    public static string Serialize(RearSuspensionSpec rearSuspension) =>
        AppJson.Serialize(rearSuspension) ??
        throw new JsonException("Rear suspension serialization produced null JSON.");

    public static RearSuspensionSpec Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("Rear suspension JSON is required.");
        }

        return AppJson.Deserialize<RearSuspensionSpec>(json) ??
               throw new JsonException("Rear suspension JSON produced no value.");
    }

    public static bool TryDeserialize(string? json, out RearSuspensionSpec? rearSuspension)
    {
        rearSuspension = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            rearSuspension = Deserialize(json);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
