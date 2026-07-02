using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Kinematics;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Infrastructure;

internal static class AppJson
{
    // LENIENT — local round-trips (DB, files, fingerprint hashing) AND the client.
    // Output must remain byte-stable (ProcessingDependencyHash depends on it).
    public static JsonSerializerOptions Options { get; } = CreateLenientOptions();

    public static AppJsonContext Context { get; } = new(CreateLenientOptions());

    // HARDENED — network-inbound deserialization on the sync server only.
    public static JsonSerializerOptions InboundOptions { get; } = CreateInboundOptions();

    public static AppJsonContext InboundContext { get; } = new(CreateInboundOptions());

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
        AddPreferenceConverters(options, AppPreferenceSerialization.CurrentVersion);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static JsonSerializerOptions CreateInboundOptions()
    {
        // .NET 10 Strict preset enables: JsonUnmappedMemberHandling.Disallow,
        // AllowDuplicateProperties=false, case-sensitive binding,
        // RespectNullableAnnotations=true, RespectRequiredConstructorParameters=true.
        // Keep the snake_case enum converter (Strict does not add it).
        JsonSerializerOptions options = new(JsonSerializerDefaults.Strict);
        AddPreferenceConverters(options, AppPreferenceSerialization.CurrentVersion);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    internal static JsonSerializerOptions CreatePreferenceOptionsForVersion(int version)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        AddPreferenceConverters(options, version);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static void AddPreferenceConverters(JsonSerializerOptions options, int version)
    {
        options.Converters.Add(new SessionPreferencesJsonConverter(version));
        options.Converters.Add(new AnalysisPreferencesJsonConverter(version));
        options.Converters.Add(new SessionLayoutPreferencesJsonConverter(version));
        options.Converters.Add(new SessionPaneSizePreferenceJsonConverter(version));
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Bike))]
[JsonSerializable(typeof(BikeExportDocument))]
[JsonSerializable(typeof(Board))]
[JsonSerializable(typeof(Setup))]
[JsonSerializable(typeof(SetupExportModel))]
[JsonSerializable(typeof(SynchronizationData))]
[JsonSerializable(typeof(AppPreferencesSyncData))]
[JsonSerializable(typeof(MapPreferencesSyncData))]
[JsonSerializable(typeof(SessionPreferencesSyncData))]
[JsonSerializable(typeof(SessionPreferences))]
[JsonSerializable(typeof(SignalDisplayPreferences))]
[JsonSerializable(typeof(AnalysisPreferences))]
[JsonSerializable(typeof(SessionProcessingPreferences))]
[JsonSerializable(typeof(SignalLayoutPreferences))]
[JsonSerializable(typeof(SignalLayoutRowPreferences))]
[JsonSerializable(typeof(SessionLayoutPreferences))]
[JsonSerializable(typeof(SessionPaneGroupPreferences))]
[JsonSerializable(typeof(SessionPaneSizePreference))]
[JsonSerializable(typeof(Dictionary<Guid, SessionPreferences>))]
[JsonSerializable(typeof(TileLayerConfig))]
[JsonSerializable(typeof(List<TileLayerConfig>))]
[JsonSerializable(typeof(TrackPoint))]
[JsonSerializable(typeof(RecordedSessionSourceTransfer))]
[JsonSerializable(typeof(SessionDataTransfer))]
[JsonSerializable(typeof(RecordedSessionSourceKind))]
[JsonSerializable(typeof(RecordedLiveCaptureSourcePayload))]
[JsonSerializable(typeof(ProcessingFingerprint))]
[JsonSerializable(typeof(Metadata))]
[JsonSerializable(typeof(RawImuData))]
[JsonSerializable(typeof(RawImuSegment))]
[JsonSerializable(typeof(RawCountSegment))]
[JsonSerializable(typeof(RawStreamGap))]
[JsonSerializable(typeof(SstFinalStatus))]
[JsonSerializable(typeof(SstStreamFinalStatus))]
[JsonSerializable(typeof(ProcessedSuspensionSegment))]
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
[JsonSerializable(typeof(Linkage), TypeInfoPropertyName = "LegacyLinkage")]
[JsonSerializable(typeof(Link))]
[JsonSerializable(typeof(Joint))]
internal partial class AppJsonContext : JsonSerializerContext;
