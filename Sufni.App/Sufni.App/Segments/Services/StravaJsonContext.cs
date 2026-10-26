using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sufni.App.Segments.Services;

internal sealed record StravaTokenResponseDto(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_at")] long ExpiresAt,
    [property: JsonPropertyName("scope")] string? Scope);

internal sealed record StravaActivitySummaryDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("start_date")] DateTimeOffset StartDate,
    [property: JsonPropertyName("elapsed_time")] int ElapsedTimeSeconds);

internal sealed record StravaDetailedActivityDto(
    [property: JsonPropertyName("segment_efforts")] List<StravaSegmentEffortDto>? SegmentEfforts);

internal sealed record StravaSegmentEffortDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("start_date")] DateTimeOffset StartDate,
    [property: JsonPropertyName("elapsed_time")] int ElapsedTimeSeconds,
    [property: JsonPropertyName("distance")] double DistanceMeters,
    [property: JsonPropertyName("segment")] StravaSummarySegmentDto? Segment);

internal sealed record StravaSummarySegmentDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("distance")] double DistanceMeters);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StravaTokenResponseDto))]
[JsonSerializable(typeof(List<StravaActivitySummaryDto>))]
[JsonSerializable(typeof(StravaDetailedActivityDto))]
internal sealed partial class StravaJsonContext : JsonSerializerContext;
