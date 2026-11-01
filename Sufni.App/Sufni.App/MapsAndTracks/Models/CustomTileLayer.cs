using System.Text.Json.Serialization;
using SQLite;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.MapsAndTracks.Models;

[Table("custom_tile_layer")]
public sealed class CustomTileLayer : Synchronizable
{
    [Column("name"), JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [Column("url_template"), JsonPropertyName("url_template")]
    public string UrlTemplate { get; set; } = string.Empty;
    [Column("attribution_text"), JsonPropertyName("attribution_text")]
    public string AttributionText { get; set; } = string.Empty;
    [Column("attribution_url"), JsonPropertyName("attribution_url")]
    public string AttributionUrl { get; set; } = string.Empty;
    [Column("max_zoom"), JsonPropertyName("max_zoom")]
    public int MaxZoom { get; set; } = 19;

    public static CustomTileLayer From(TileLayerConfig config) => new()
    {
        Id = config.Id, Name = config.Name, UrlTemplate = config.UrlTemplate,
        AttributionText = config.AttributionText, AttributionUrl = config.AttributionUrl, MaxZoom = config.MaxZoom
    };
}
