using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.SessionGraph;
using Sufni.Kinematics;
using Sufni.Telemetry;

namespace Sufni.App.Models;

internal static class AppJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static AppJsonContext Context { get; } = new(CreateOptions());

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

    private static JsonSerializerOptions CreateOptions()
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
[JsonSerializable(typeof(BikeExportModel))]
[JsonSerializable(typeof(Board))]
[JsonSerializable(typeof(Setup))]
[JsonSerializable(typeof(SetupExportModel))]
[JsonSerializable(typeof(SynchronizationData))]
[JsonSerializable(typeof(AppPreferencesSyncData))]
[JsonSerializable(typeof(MapPreferencesSyncData))]
[JsonSerializable(typeof(SessionPreferencesSyncData))]
[JsonSerializable(typeof(SessionPreferences))]
[JsonSerializable(typeof(SessionPlotPreferences))]
[JsonSerializable(typeof(SessionStatisticsPreferences))]
[JsonSerializable(typeof(SessionProcessingPreferences))]
[JsonSerializable(typeof(SessionGraphPreferences))]
[JsonSerializable(typeof(SessionGraphRowPreferences))]
[JsonSerializable(typeof(Dictionary<Guid, SessionPreferences>))]
[JsonSerializable(typeof(TileLayerConfig))]
[JsonSerializable(typeof(List<TileLayerConfig>))]
[JsonSerializable(typeof(TrackPoint))]
[JsonSerializable(typeof(RecordedSessionSourceTransfer))]
[JsonSerializable(typeof(RecordedSessionSourceKind))]
[JsonSerializable(typeof(RecordedLiveCaptureSourcePayload))]
[JsonSerializable(typeof(ProcessingFingerprint))]
[JsonSerializable(typeof(Metadata))]
[JsonSerializable(typeof(RawImuData))]
[JsonSerializable(typeof(ImuMetaEntry))]
[JsonSerializable(typeof(ImuRecord))]
[JsonSerializable(typeof(GpsRecord))]
[JsonSerializable(typeof(MarkerData))]
[JsonSerializable(typeof(ushort[]))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(List<TrackPoint>))]
[JsonSerializable(typeof(PairingRequest))]
[JsonSerializable(typeof(PairingConfirm))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(UnpairRequest))]
[JsonSerializable(typeof(SensorConfiguration))]
[JsonSerializable(typeof(LinearForkSensorConfiguration))]
[JsonSerializable(typeof(RotationalForkSensorConfiguration))]
[JsonSerializable(typeof(LinearShockSensorConfiguration))]
[JsonSerializable(typeof(RotationalShockSensorConfiguration))]
[JsonSerializable(typeof(RearSuspensionKind))]
[JsonSerializable(typeof(LeverageRatio))]
[JsonSerializable(typeof(LeverageRatioPoint))]
[JsonSerializable(typeof(List<LeverageRatioPoint>))]
[JsonSerializable(typeof(Linkage))]
[JsonSerializable(typeof(Link))]
[JsonSerializable(typeof(Joint))]
internal partial class AppJsonContext : JsonSerializerContext;
