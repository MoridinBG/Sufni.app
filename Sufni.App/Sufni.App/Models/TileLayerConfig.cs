using System;
using System.Text.Json.Serialization;

namespace Sufni.App.Models;

public class TileLayerConfig
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("url_template")]
    public string UrlTemplate { get; set; } = null!;

    [JsonPropertyName("attribution_text")]
    public string AttributionText { get; set; } = null!;

    [JsonPropertyName("attribution_url")]
    public string AttributionUrl { get; set; } = null!;

    [JsonPropertyName("max_zoom")]
    public int MaxZoom { get; set; } = 19;

    [JsonPropertyName("is_custom")]
    public bool IsCustom { get; set; }

    public override string ToString() => Name;
}
