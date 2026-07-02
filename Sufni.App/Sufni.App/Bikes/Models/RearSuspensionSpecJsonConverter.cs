using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

public sealed class RearSuspensionSpecJsonConverter : JsonConverter<RearSuspensionSpec>
{
    private const string KindPropertyName = "kind";
    private const string LinkagePropertyName = "linkage";
    private const string LeverageRatioPropertyName = "leverage_ratio";

    private const string HardtailKind = "hardtail";
    private const string LinkageDraftKind = "linkage_draft";
    private const string LeverageRatioDraftKind = "leverage_ratio_draft";
    private const string LinkageKind = "linkage";
    private const string LeverageRatioKind = "leverage_ratio";

    public override RearSuspensionSpec Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Rear suspension must be a JSON object.");
        }

        var hasKind = false;
        var hasLinkage = false;
        var hasLeverageRatio = false;
        JsonElement kindElement = default;
        JsonElement linkageElement = default;
        JsonElement leverageRatioElement = default;

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case KindPropertyName:
                    if (hasKind)
                    {
                        throw new JsonException("Rear suspension cannot contain duplicate kind properties.");
                    }

                    hasKind = true;
                    kindElement = property.Value;
                    break;
                case LinkagePropertyName:
                    if (hasLinkage)
                    {
                        throw new JsonException("Rear suspension cannot contain duplicate linkage payloads.");
                    }

                    hasLinkage = true;
                    linkageElement = property.Value;
                    break;
                case LeverageRatioPropertyName:
                    if (hasLeverageRatio)
                    {
                        throw new JsonException("Rear suspension cannot contain duplicate leverage ratio payloads.");
                    }

                    hasLeverageRatio = true;
                    leverageRatioElement = property.Value;
                    break;
                default:
                    throw new JsonException($"Unknown rear suspension property '{property.Name}'.");
            }
        }

        if (!hasKind || kindElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Rear suspension kind is required.");
        }

        var kind = kindElement.GetString();
        if (hasLinkage && hasLeverageRatio)
        {
            throw new JsonException("Rear suspension cannot contain multiple payloads.");
        }

        return kind switch
        {
            HardtailKind => ReadPayloadless<RearSuspensionSpec.Hardtail>(hasLinkage, hasLeverageRatio),
            LinkageDraftKind => ReadPayloadless<RearSuspensionSpec.LinkageDraft>(hasLinkage, hasLeverageRatio),
            LeverageRatioDraftKind => ReadPayloadless<RearSuspensionSpec.LeverageRatioDraft>(hasLinkage, hasLeverageRatio),
            LinkageKind => ReadLinkage(hasLinkage, linkageElement, hasLeverageRatio, options),
            LeverageRatioKind => ReadLeverageRatio(hasLeverageRatio, leverageRatioElement, hasLinkage, options),
            _ => throw new JsonException($"Unknown rear suspension kind '{kind}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, RearSuspensionSpec value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case RearSuspensionSpec.Hardtail:
                writer.WriteString(KindPropertyName, HardtailKind);
                break;
            case RearSuspensionSpec.LinkageDraft:
                writer.WriteString(KindPropertyName, LinkageDraftKind);
                break;
            case RearSuspensionSpec.LeverageRatioDraft:
                writer.WriteString(KindPropertyName, LeverageRatioDraftKind);
                break;
            case RearSuspensionSpec.Linkage linkage:
                writer.WriteString(KindPropertyName, LinkageKind);
                writer.WritePropertyName(LinkagePropertyName);
                JsonSerializer.Serialize(writer, linkage.Spec, options);
                break;
            case RearSuspensionSpec.LeverageRatio leverageRatio:
                writer.WriteString(KindPropertyName, LeverageRatioKind);
                writer.WritePropertyName(LeverageRatioPropertyName);
                JsonSerializer.Serialize(writer, leverageRatio.Spec, options);
                break;
            default:
                throw new JsonException($"Unknown rear suspension type '{value.GetType().Name}'.");
        }

        writer.WriteEndObject();
    }

    private static RearSuspensionSpec ReadPayloadless<T>(
        bool hasLinkage,
        bool hasLeverageRatio)
        where T : RearSuspensionSpec, new()
    {
        if (hasLinkage || hasLeverageRatio)
        {
            throw new JsonException("Rear suspension kind does not allow a payload.");
        }

        return new T();
    }

    private static RearSuspensionSpec ReadLinkage(
        bool hasLinkage,
        JsonElement linkageElement,
        bool hasLeverageRatio,
        JsonSerializerOptions options)
    {
        if (hasLeverageRatio)
        {
            throw new JsonException("Linkage rear suspension cannot contain a leverage ratio payload.");
        }

        if (!hasLinkage || linkageElement.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException("Linkage rear suspension requires a linkage payload.");
        }

        var linkage = JsonSerializer.Deserialize<LinkageSpec>(linkageElement.GetRawText(), options);
        return linkage is null
            ? throw new JsonException("Linkage rear suspension requires a linkage payload.")
            : new RearSuspensionSpec.Linkage(linkage);
    }

    private static RearSuspensionSpec ReadLeverageRatio(
        bool hasLeverageRatio,
        JsonElement leverageRatioElement,
        bool hasLinkage,
        JsonSerializerOptions options)
    {
        if (hasLinkage)
        {
            throw new JsonException("Leverage ratio rear suspension cannot contain a linkage payload.");
        }

        if (!hasLeverageRatio || leverageRatioElement.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException("Leverage ratio rear suspension requires a leverage ratio payload.");
        }

        var leverageRatio = JsonSerializer.Deserialize<LeverageRatioSpec>(leverageRatioElement.GetRawText(), options);
        return leverageRatio is null
            ? throw new JsonException("Leverage ratio rear suspension requires a leverage ratio payload.")
            : new RearSuspensionSpec.LeverageRatio(leverageRatio);
    }
}
