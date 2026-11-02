using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;

namespace Sufni.App.Bikes.Services;

/// <summary>Reads shared bike metadata without materializing image payloads.</summary>
public interface IBikeMetadataReader
{
    Task<IReadOnlyList<BikeSnapshot>> GetAllAsync();
}

internal sealed class BikeMetadataReader(SqliteConnectionContext connectionContext) : IBikeMetadataReader
{
    public async Task<IReadOnlyList<BikeSnapshot>> GetAllAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<BikeMetadataRow>("""
            SELECT id, name, head_angle, fork_stroke, shock_stroke, rear_suspension,
                   front_compression_damping_cutoff_mm_per_second, front_rebound_damping_cutoff_mm_per_second,
                   rear_compression_damping_cutoff_mm_per_second, rear_rebound_damping_cutoff_mm_per_second,
                   pixels_to_millimeters, front_wheel_diameter, rear_wheel_diameter,
                   front_wheel_rim_size, front_wheel_tire_width, rear_wheel_rim_size, rear_wheel_tire_width,
                   image_rotation_degrees, COALESCE(length(image), 0) > 0 AS has_image, updated
            FROM bike WHERE deleted IS NULL
            """);
        return rows.Select(row => BikeSnapshot.From(row) with { HasImage = row.HasImage }).ToArray();
    }

    private sealed class BikeMetadataRow : Bike
    {
        [Column("has_image")]
        public bool HasImage { get; set; }
    }
}
