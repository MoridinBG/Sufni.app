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

    [Fact]
    public async Task GetSynchronizationDataAsync_IncludesTrackReferencedByChangedSession()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "sync-data.db");
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetSessionsAsync();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Track
                {
                    Id = trackId,
                    Points =
                    [
                        new TrackPoint(100, 1, 1, 10),
                        new TrackPoint(101, 2, 2, 11)
                    ],
                    Updated = 50,
                    ClientUpdated = 50
                });

                connection.Insert(new Session(sessionId, "session", "desc", null, 100)
                {
                    FullTrack = trackId,
                    Updated = 150,
                    ClientUpdated = 150
                });
            }

            var syncData = await database.GetSynchronizationDataAsync(100);

            Assert.Single(syncData.Sessions);
            Assert.Single(syncData.Tracks);
            Assert.Equal(trackId, syncData.Tracks[0].Id);
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
    public async Task PutAsync_Throws_WhenLiveTrackHasNoPoints()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "track-validation.db");

        try
        {
            var database = new SqLiteDatabaseService(databasePath);

            await Assert.ThrowsAsync<InvalidOperationException>(() => database.PutAsync(new Track { Points = [] }));
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
    public async Task ApplyRemoteSynchronizationDataAsync_AllowsDeletedTrackWithoutPoints()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "deleted-track-sync.db");
        var trackId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);

            await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
            {
                Tracks =
                [
                    new Track
                    {
                        Id = trackId,
                        Points = [],
                        Deleted = 100,
                        Updated = 100,
                        ClientUpdated = 100
                    }
                ]
            });

            using var verificationConnection = new SQLiteConnection(databasePath);
            var track = verificationConnection.Table<Track>().Single(t => t.Id == trackId);

            Assert.Equal(100, track.Deleted);
            Assert.False(track.HasPoints);
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
    public async Task GetSessionPsstAsync_ReturnsNull_WhenSessionHasNoProcessedData()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "session-psst.db");
        var sessionId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100));

            var telemetryData = await database.GetSessionPsstAsync(sessionId);

            Assert.Null(telemetryData);
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
    public async Task PatchSessionPsstAsync_FlipsHasProcessedData_ForChangedSessionQueries()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "session-psst-patch.db");
        var sessionId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
            {
                Sessions = [new Session(sessionId, "remote", "desc", null, 100) { Updated = 10, ClientUpdated = 10 }]
            });

            await database.PatchSessionPsstAsync(sessionId, [1, 2, 3]);

            var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0));

            Assert.True(changedSession.HasProcessedData);
            Assert.Equal([1, 2, 3], await database.GetSessionRawPsstAsync(sessionId));
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
    public async Task ApplyRemoteSynchronizationDataAsync_UpdatesSessionSyncFields_WithoutClearingPsst()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "remote-sync.db");
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var originalPsst = new byte[] { 1, 2, 3, 4 };

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetSessionsAsync();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Session(sessionId, "local", "local desc", null, 50)
                {
                    ProcessedData = originalPsst,
                    Updated = 1,
                    ClientUpdated = 1
                });
            }

            var remoteSession = new Session(sessionId, "remote", "remote desc", setupId, 1234)
            {
                FullTrack = trackId,
                Track =
                [
                    new TrackPoint(1234, 1, 1, 100),
                    new TrackPoint(1235, 2, 2, 101)
                ],
                FrontSpringRate = "50",
                RearSpringRate = "60",
                Updated = 99,
                ClientUpdated = 88
            };

            var remoteTrack = new Track
            {
                Id = trackId,
                Points =
                [
                    new TrackPoint(1234, 10, 10, 100),
                    new TrackPoint(1235, 20, 20, 101)
                ],
                Updated = 99,
                ClientUpdated = 88
            };

            await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
            {
                Sessions = [remoteSession],
                Tracks = [remoteTrack]
            });

            var session = await database.GetSessionAsync(sessionId);
            var sessionTrack = await database.GetSessionTrackAsync(sessionId);
            var rawPsst = await database.GetSessionRawPsstAsync(sessionId);
            var fullTrack = await database.GetAsync<Track>(trackId);

            Assert.NotNull(session);
            Assert.Equal("remote", session!.Name);
            Assert.Equal("remote desc", session.Description);
            Assert.Equal(setupId, session.Setup);
            Assert.Equal(1234, session.Timestamp);
            Assert.Equal(trackId, session.FullTrack);
            Assert.Equal(99, session.Updated);
            Assert.Equal("50", session.FrontSpringRate);
            Assert.Equal("60", session.RearSpringRate);
            Assert.NotNull(sessionTrack);
            Assert.Equal(2, sessionTrack!.Count);
            Assert.Equal(originalPsst, rawPsst);
            Assert.NotNull(fullTrack);
            Assert.Equal(2, fullTrack!.Points.Count);
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
    public async Task PutAsync_ReusesSoftDeletedBoardRow_InsteadOfInserting()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "board-revive.db");
        var boardId = Guid.NewGuid();
        var revivedSetupId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetAllAsync<Board>();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Board(boardId, null)
                {
                    Updated = 10,
                    ClientUpdated = 10,
                    Deleted = 10
                });
            }

            await database.PutAsync(new Board(boardId, revivedSetupId));

            using var verificationConnection = new SQLiteConnection(databasePath);
            var boards = verificationConnection.Table<Board>().Where(b => b.Id == boardId).ToList();

            var board = Assert.Single(boards);
            Assert.Equal(revivedSetupId, board.SetupId);
            Assert.Null(board.Deleted);
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
    public async Task PutSessionAsync_ReusesSoftDeletedRow_AndPreservesExistingBinaryData()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "session-revive.db");
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var originalPsst = new byte[] { 9, 8, 7 };
        var originalTrack = new List<TrackPoint>
        {
            new(10, 1, 1, 0),
            new(11, 2, 2, 0)
        };

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetSessionsAsync();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Session(sessionId, "old", "old desc", null, 50)
                {
                    ProcessedData = originalPsst,
                    Track = originalTrack,
                    Updated = 10,
                    ClientUpdated = 10,
                    Deleted = 10
                });
            }

            await database.PutSessionAsync(new Session(sessionId, "new", "new desc", setupId, 1234)
            {
                FullTrack = fullTrackId
            });

            var session = await database.GetSessionAsync(sessionId);
            var rawPsst = await database.GetSessionRawPsstAsync(sessionId);
            var sessionTrack = await database.GetSessionTrackAsync(sessionId);

            Assert.NotNull(session);
            Assert.Equal("new", session!.Name);
            Assert.Equal("new desc", session.Description);
            Assert.Equal(setupId, session.Setup);
            Assert.Equal(1234, session.Timestamp);
            Assert.Equal(fullTrackId, session.FullTrack);
            Assert.True(session.HasProcessedData);
            Assert.Null(session.Deleted);
            Assert.Equal(originalPsst, rawPsst);
            Assert.NotNull(sessionTrack);
            Assert.Equal(2, sessionTrack!.Count);
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
    public async Task PatchSessionTrackAsync_UpdatesTrackAndBumpsSessionUpdated()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "session-track-patch.db");
        var sessionId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetSessionsAsync();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Session(sessionId, "session", "desc", null, 100)
                {
                    Updated = 1,
                    ClientUpdated = 1
                });
            }

            var before = await database.GetSessionAsync(sessionId);

            await database.PatchSessionTrackAsync(sessionId,
            [
                new TrackPoint(100, 1, 1, 0),
                new TrackPoint(101, 2, 2, 0)
            ]);

            var after = await database.GetSessionAsync(sessionId);
            var track = await database.GetSessionTrackAsync(sessionId);

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.NotNull(track);
            Assert.Equal(2, track!.Count);
            Assert.True(after!.Updated > before!.Updated);
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
    public async Task AssociateSessionWithTrackAsync_UpdatesAssociationAndBumpsSessionUpdated()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "session-track-association.db");
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetSessionsAsync();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Session(sessionId, "session", "desc", null, 100)
                {
                    Updated = 1,
                    ClientUpdated = 1
                });
            }

            await database.PutAsync(new Track
            {
                Id = trackId,
                Points =
                [
                    new TrackPoint(90, 1, 1, 0),
                    new TrackPoint(110, 2, 2, 0)
                ]
            });

            var before = await database.GetSessionAsync(sessionId);

            var associatedTrackId = await database.AssociateSessionWithTrackAsync(sessionId);

            var after = await database.GetSessionAsync(sessionId);

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.Equal(trackId, associatedTrackId);
            Assert.Equal(trackId, after!.FullTrack);
            Assert.True(after.Updated > before!.Updated);
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
    public async Task MergeAllAsync_DoesNotResurrectDeletedBoard_WhenIncomingUpdateIsNotDeleted()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "merge-delete.db");
        var boardId = Guid.NewGuid();
        var deletedSetupId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetAllAsync<Board>();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Board(boardId, deletedSetupId)
                {
                    Updated = 110,
                    ClientUpdated = 100,
                    Deleted = 100
                });
            }

            await database.MergeAllAsync(new SynchronizationData
            {
                Boards =
                [
                    new Board(boardId, Guid.NewGuid())
                    {
                        Updated = 150,
                        ClientUpdated = 150,
                        Deleted = null
                    }
                ]
            });

            using var verificationConnection = new SQLiteConnection(databasePath);
            var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

            Assert.Equal(deletedSetupId, board.SetupId);
            Assert.Equal(100, board.Deleted);
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
    public async Task MergeAllAsync_DoesNotApplyStaleDeleteOverNewerLiveBoard()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "merge-stale-delete.db");
        var boardId = Guid.NewGuid();
        var liveSetupId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetAllAsync<Board>();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Board(boardId, liveSetupId)
                {
                    Updated = 210,
                    ClientUpdated = 200
                });
            }

            await database.MergeAllAsync(new SynchronizationData
            {
                Boards =
                [
                    new Board(boardId, null)
                    {
                        Updated = 150,
                        ClientUpdated = 150,
                        Deleted = 150
                    }
                ]
            });

            using var verificationConnection = new SQLiteConnection(databasePath);
            var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

            Assert.Equal(liveSetupId, board.SetupId);
            Assert.Null(board.Deleted);
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
    public async Task MergeAllAsync_AppliesNewerDeleteOverOlderLiveBoard()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "merge-new-delete.db");
        var boardId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetAllAsync<Board>();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Board(boardId, Guid.NewGuid())
                {
                    Updated = 110,
                    ClientUpdated = 100
                });
            }

            await database.MergeAllAsync(new SynchronizationData
            {
                Boards =
                [
                    new Board(boardId, null)
                    {
                        Updated = 150,
                        ClientUpdated = 150,
                        Deleted = 150
                    }
                ]
            });

            using var verificationConnection = new SQLiteConnection(databasePath);
            var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

            Assert.Equal(150, board.Deleted);
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
    public async Task DeleteAsync_DoesNotRestampExistingTombstone()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"sufni-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "delete-tombstone.db");
        var boardId = Guid.NewGuid();

        try
        {
            var database = new SqLiteDatabaseService(databasePath);
            _ = await database.GetAllAsync<Board>();

            using (var connection = new SQLiteConnection(databasePath))
            {
                connection.Insert(new Board(boardId, null)
                {
                    Updated = 10,
                    ClientUpdated = 10,
                    Deleted = 100
                });
            }

            await database.DeleteAsync(new Board(boardId, null));

            using var verificationConnection = new SQLiteConnection(databasePath);
            var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

            Assert.Equal(100, board.Deleted);
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