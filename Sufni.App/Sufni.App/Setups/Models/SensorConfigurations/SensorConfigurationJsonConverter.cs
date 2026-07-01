using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Sufni.App.Setups.Models.SensorConfigurations;

// Single-pass polymorphic converter. Peeks "type" once via a reader clone, then
// deserializes the concrete type once. Attribute polymorphism is impossible
// (LinearShock & LinearShockStroke share LinearShockSensorConfiguration). Write path
// defers to the concrete type's source-gen metadata → bytes are unchanged.
internal sealed class SensorConfigurationJsonConverter : JsonConverter<SensorConfiguration>
{
    public override SensorConfiguration? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var discriminator = ReadDiscriminator(reader, options);   // reader is a struct copy
        var concreteType = discriminator switch
        {
            SensorType.LinearFork        => typeof(LinearForkSensorConfiguration),
            SensorType.RotationalFork    => typeof(RotationalForkSensorConfiguration),
            SensorType.LinearShock       => typeof(LinearShockSensorConfiguration),
            SensorType.LinearShockStroke => typeof(LinearShockSensorConfiguration),
            SensorType.RotationalShock   => typeof(RotationalShockSensorConfiguration),
            _ => null,
        };
        if (concreteType is null) { reader.Skip(); return null; }   // preserve "unknown => null"
        var typeInfo = options.GetTypeInfo(concreteType);
        return (SensorConfiguration?)JsonSerializer.Deserialize(ref reader, typeInfo);
    }

    public override void Write(Utf8JsonWriter writer, SensorConfiguration value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options.GetTypeInfo(value.GetType()));

    private static SensorType? ReadDiscriminator(Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) return null;
        var depth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth) break;
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != depth + 1) continue;
            var isType = reader.ValueTextEquals("type") ||
                         string.Equals(reader.GetString(), "type", StringComparison.OrdinalIgnoreCase);
            if (!isType) { reader.Skip(); continue; }
            reader.Read();
            var enumConverter = (JsonConverter<SensorType>)options.GetConverter(typeof(SensorType));
            return enumConverter.Read(ref reader, typeof(SensorType), options);
        }
        return null;
    }
}
