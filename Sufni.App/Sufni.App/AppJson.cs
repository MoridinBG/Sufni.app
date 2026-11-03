using Sufni.App.Bikes.Services;
using Sufni.App.Setups.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App;

internal static class AppJson
{
    // LENIENT — local round-trips (DB, files, fingerprint hashing) AND the client.
    // Output must remain byte-stable (ProcessingDependencyHash depends on it).
    public static JsonSerializerOptions Options { get; } = CreateLenientOptions();

    public static AppJsonContext Context { get; } = new(CreateLenientOptions());

    public static string Serialize<T>(T? value)
    {
        return JsonSerializer.Serialize(value, typeof(T), Context);
    }

    public static T? Deserialize<T>(string json) where T : class
    {
        return (T?)JsonSerializer.Deserialize(json, typeof(T), Context);
    }

    public static ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public static Task SerializeAsync<T>(Stream stream, T? value, CancellationToken cancellationToken = default)
    {
        return JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
    }

    public static string SerializeIndented<T>(T? value)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        JsonSerializer.Serialize(writer, value, typeof(T), Context);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonSerializerOptions CreateLenientOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }



}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Bike))]
[JsonSerializable(typeof(BikeExportDocument))]
[JsonSerializable(typeof(Board))]
[JsonSerializable(typeof(Setup))]
[JsonSerializable(typeof(SetupExportModel))]
[JsonSerializable(typeof(SynchronizationData))]
[JsonSerializable(typeof(SessionProcessedGeneration))]
[JsonSerializable(typeof(GpsMetricsKey))]
[JsonSerializable(typeof(TrackPoint))]
[JsonSerializable(typeof(RecordedSessionSourceKind))]
[JsonSerializable(typeof(ProcessingFingerprint))]
[JsonSerializable(typeof(RecordedSessionDerivationWindow))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(List<TrackPoint>))]
[JsonSerializable(typeof(PairingRequest))]
[JsonSerializable(typeof(PairingConfirm))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(SynchronizationRunStart))]
[JsonSerializable(typeof(SynchronizationRunStarted))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(UnpairRequest))]
[JsonSerializable(typeof(SensorConfiguration))]
[JsonSerializable(typeof(LinearForkSensorConfiguration))]
[JsonSerializable(typeof(RotationalForkSensorConfiguration))]
[JsonSerializable(typeof(LinearShockSensorConfiguration))]
[JsonSerializable(typeof(RotationalShockSensorConfiguration))]
[JsonSerializable(typeof(RearSuspensionSpec))]
[JsonSerializable(typeof(RearSuspensionSpec.Hardtail))]
[JsonSerializable(typeof(RearSuspensionSpec.LinkageDraft))]
[JsonSerializable(typeof(RearSuspensionSpec.LeverageRatioDraft))]
[JsonSerializable(typeof(RearSuspensionSpec.Linkage), TypeInfoPropertyName = "RearSuspensionSpecLinkage")]
[JsonSerializable(typeof(RearSuspensionSpec.LeverageRatio), TypeInfoPropertyName = "RearSuspensionSpecLeverageRatio")]
[JsonSerializable(typeof(LinkageSpec))]
[JsonSerializable(typeof(JointSpec))]
[JsonSerializable(typeof(LinkSpec))]
[JsonSerializable(typeof(LeverageRatioSpec))]
[JsonSerializable(typeof(LeverageRatioPoint))]
[JsonSerializable(typeof(List<LeverageRatioPoint>))]
[JsonSerializable(typeof(WheelSpec), TypeInfoPropertyName = "BikeWheelSpec")]
[JsonSerializable(typeof(DampingSpeedCutoffs))]
[JsonSerializable(typeof(DampingSpeedCutoffSide))]
internal partial class AppJsonContext : JsonSerializerContext;
