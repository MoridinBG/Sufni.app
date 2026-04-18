using System.IO;
using SQLite;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Services;

public class SQLiteDatabaseServiceTests
{
    [Fact]
    public async Task UpdateLastSyncTimeAsync_InsertsRow_WhenMissing()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "sync.db");

        try
        {
            var database = new SqLiteDatabaseService(databasePath);

            await database.UpdateLastSyncTimeAsync("https://sync.test");

            var lastSyncTime = await database.GetLastSyncTimeAsync("https://sync.test");
            Assert.True(lastSyncTime > 0);

            using var connection = new SQLiteConnection(databasePath);
            var rows = connection.Table<Synchronization>()
                .Where(s => s.ServerUrl == "https://sync.test")
                .ToList();
            Assert.Single(rows);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateLastSyncTimeAsync_UpdatesExistingRow_WithoutDuplicatingIt()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "sync.db");

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetLastSyncTimeAsync("https://sync.test");

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Synchronization
                {
                    ServerUrl = "https://sync.test",
                    LastSyncTime = 1
                });
            }

            await database.UpdateLastSyncTimeAsync("https://sync.test");

            var lastSyncTime = await database.GetLastSyncTimeAsync("https://sync.test");
            Assert.True(lastSyncTime > 1);

            using var verificationConnection = new SQLiteConnection(databasePath);
            var rows = verificationConnection.Table<Synchronization>()
                .Where(s => s.ServerUrl == "https://sync.test")
                .ToList();
            Assert.Single(rows);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Initialization_BackfillsLegacyLinkageRows_AndKeepsBackfillIdempotent()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "legacy.db");
        var legacyBikeId = Guid.NewGuid();

        try
        {
            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Execute(
                    """
                    CREATE TABLE bike (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        head_angle REAL NOT NULL,
                        fork_stroke REAL,
                        shock_stroke REAL,
                        linkage TEXT,
                        pixels_to_millimeters REAL NOT NULL DEFAULT 0,
                        front_wheel_diameter REAL,
                        rear_wheel_diameter REAL,
                        front_wheel_rim_size INTEGER,
                        front_wheel_tire_width REAL,
                        rear_wheel_rim_size INTEGER,
                        rear_wheel_tire_width REAL,
                        image_rotation_degrees REAL NOT NULL DEFAULT 0,
                        image BLOB,
                        updated INTEGER NOT NULL,
                        client_updated INTEGER,
                        deleted INTEGER
                    )
                    """);

                connection.Execute(
                    "INSERT INTO bike (id, name, head_angle, fork_stroke, shock_stroke, linkage, pixels_to_millimeters, image_rotation_degrees, image, updated) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    legacyBikeId.ToString(),
                    "legacy linkage bike",
                    64.0,
                    150.0,
                    0.5,
                    TestSnapshots.FullSuspensionLinkage().ToJson(),
                    0.0,
                    0.0,
                    Array.Empty<byte>(),
                    1);
            }

            var firstRun = new SqLiteDatabaseService(databasePath);
            var firstRunBikes = await firstRun.GetAllAsync<Bike>();

            using (var verificationConnection = new SQLiteConnection(databasePath))
            {
                var columns = verificationConnection.Query<TableColumnInfo>("PRAGMA table_info(bike)");

                Assert.Contains(columns, column => column.Name == "rear_suspension_kind");
                Assert.Contains(columns, column => column.Name == "leverage_ratio");
            }

            var firstRunBike = Assert.Single(firstRunBikes);
            Assert.Equal(legacyBikeId, firstRunBike.Id);
            Assert.Equal(RearSuspensionKind.Linkage, firstRunBike.RearSuspensionKind);
            Assert.NotNull(firstRunBike.Linkage);

            var secondRun = new SqLiteDatabaseService(databasePath);
            var secondRunBikes = await secondRun.GetAllAsync<Bike>();

            var secondRunBike = Assert.Single(secondRunBikes);
            Assert.Equal(legacyBikeId, secondRunBike.Id);
            Assert.Equal(RearSuspensionKind.Linkage, secondRunBike.RearSuspensionKind);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private sealed class TableColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}