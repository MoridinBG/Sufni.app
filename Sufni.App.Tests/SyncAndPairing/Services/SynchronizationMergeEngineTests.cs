using Sufni.App.Sessions.Services;
using SQLite;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizationMergeEngineTests
{
    [Theory]
    [InlineData(2, 100, "a", null, 1, 200, "z", null, false)]
    [InlineData(2, 100, "a", null, 2, 101, "a", null, true)]
    [InlineData(2, 100, "a", null, 2, 100, "z", null, true)]
    [InlineData(2, 200, "z", null, 2, 100, "a", 100, true)]
    [InlineData(2, 100, "a", 100, 2, 101, "a", 101, true)]
    [InlineData(2, 100, "a", 100, 2, 100, "z", 99, true)]
    [InlineData(3, 1, "a", null, 2, 200, "z", 200, false)]
    public async Task BothMergeDirections_ConvergeInEitherArrivalOrder_AndRepeatedExchangeWritesNothing(
        long firstVersion, long firstUpdated, string firstWriter, int? firstDeleted,
        long secondVersion, long secondUpdated, string secondWriter, int? secondDeleted, bool secondWins)
    {
        using var hubFile = new TempDatabase();
        using var clientFile = new TempDatabase();
        var hub = new TestPersistenceHarness(hubFile.DatabasePath);
        var client = new TestPersistenceHarness(clientFile.DatabasePath);
        var id = Guid.NewGuid();
        await hub.PutAsync(new Board(id, null));
        await client.PutAsync(new Board(id, null));
        var first = new Board(id, Guid.NewGuid()) { Version = firstVersion, Updated = firstUpdated, UpdatedBy = firstWriter, Deleted = firstDeleted };
        var second = new Board(id, Guid.NewGuid()) { Version = secondVersion, Updated = secondUpdated, UpdatedBy = secondWriter, Deleted = secondDeleted };
        var expected = secondWins ? second : first;

        await hub.MergeAllAsync(new SynchronizationData { Boards = [first] });
        await hub.MergeAllAsync(new SynchronizationData { Boards = [second] });
        await client.ApplyRemoteSynchronizationDataAsync(new SynchronizationData { Boards = [second] });
        await client.ApplyRemoteSynchronizationDataAsync(new SynchronizationData { Boards = [first] });

        foreach (var database in new[] { hub, client })
        {
            var connection = await database.GetInitializedConnectionAsync();
            var row = await connection.GetAsync<Board>(id);
            Assert.Equal((expected.Version, expected.Updated, expected.UpdatedBy, expected.Deleted),
                (row.Version, row.Updated, row.UpdatedBy, row.Deleted));
            if (expected.Deleted is null) Assert.Equal(expected.SetupId, row.SetupId);
        }
        var hubChanges = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(await hub.GetSynchronizationDataAsync(0, long.MaxValue)))!;
        var clientChanges = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(await client.GetSynchronizationDataAsync(0, long.MaxValue)))!;
        var hubConnection = await hub.GetInitializedConnectionAsync();
        var clientConnection = await client.GetInitializedConnectionAsync();
        var beforeHub = await hubConnection.ExecuteScalarAsync<long>("SELECT total_changes()");
        var beforeClient = await clientConnection.ExecuteScalarAsync<long>("SELECT total_changes()");

        await hub.MergeAllAsync(clientChanges);
        await client.ApplyRemoteSynchronizationDataAsync(hubChanges);

        Assert.Equal(beforeHub, await hubConnection.ExecuteScalarAsync<long>("SELECT total_changes()"));
        Assert.Equal(beforeClient, await clientConnection.ExecuteScalarAsync<long>("SELECT total_changes()"));
    }

    [Fact]
    public async Task PurgedDeletion_TravelsAsLedgerAndRejectsOldContent_UntilExplicitHigherVersionRestore()
    {
        using var sourceFile = new TempDatabase();
        using var peerFile = new TempDatabase();
        var clock = PersistenceTestData.CreateTimeProvider();
        var source = new TestPersistenceHarness(sourceFile.DatabasePath, clock, "source");
        var peer = new TestPersistenceHarness(peerFile.DatabasePath, clock, "peer");
        var original = new Board(Guid.NewGuid(), Guid.NewGuid());
        await source.PutAsync(original);
        var oldPayload = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(await source.GetSynchronizationDataAsync(0, long.MaxValue)))!;
        await peer.MergeAllAsync(oldPayload);
        await source.DeleteAsync(original);
        clock.Advance(TimeSpan.FromDays(2));
        source = new TestPersistenceHarness(sourceFile.DatabasePath, clock, "source");
        var deletionPayload = await source.GetSynchronizationDataAsync(0, long.MaxValue);
        Assert.Empty(deletionPayload.Boards);
        Assert.Single(deletionPayload.Deletions);

        await peer.ApplyRemoteSynchronizationDataAsync(AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(deletionPayload))!);
        await peer.MergeAllAsync(oldPayload);

        Assert.Null(await peer.GetAsync<Board>(original.Id));
        var restored = new Board(original.Id, Guid.NewGuid());
        await source.PutAsync(restored);
        await peer.MergeAllAsync(await source.GetSynchronizationDataAsync(0, long.MaxValue));
        var received = (await peer.GetAsync<Board>(original.Id))!;
        Assert.Equal(3, received.Version);
        Assert.Equal(restored.SetupId, received.SetupId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectionalCursorUpdates_InsertOrUpdateSingleRow(bool seedExistingRow)
    {
        using var tempDatabase = new TempDatabase("sync.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetLastPushTimeAsync("https://sync.test");
        if (seedExistingRow)
        {
            using var connection = new SQLiteConnection(databasePath);
            connection.Insert(new Synchronization
            {
                ServerUrl = "https://sync.test",
                LastPushTime = 1,
                LastPullTime = 2,
            });
        }

        await database.UpdateLastPushTimeAsync("https://sync.test", 11);
        await database.UpdateLastPullTimeAsync("https://sync.test", 12);

        Assert.Equal(11, await database.GetLastPushTimeAsync("https://sync.test"));
        Assert.Equal(12, await database.GetLastPullTimeAsync("https://sync.test"));

        using var verificationConnection = new SQLiteConnection(databasePath);
        var rows = verificationConnection.Table<Synchronization>()
            .Where(s => s.ServerUrl == "https://sync.test")
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task LegacySingleCursor_IsResetForProtocolV3Replay()
    {
        using var tempDatabase = new TempDatabase("legacy-sync.db");
        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Execute(
                "CREATE TABLE sync (server_url TEXT PRIMARY KEY, last_sync_time INTEGER NOT NULL)");
            connection.Execute(
                "INSERT INTO sync (server_url, last_sync_time) VALUES (?, ?)",
                "https://sync.test",
                123);
        }

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);

        Assert.Equal(0, await database.GetLastPushTimeAsync("https://sync.test"));
        Assert.Equal(0, await database.GetLastPullTimeAsync("https://sync.test"));
    }

    [Fact]
    public async Task GetChangedAsync_ReplaysCursorSecondAndExcludesFutureRows()
    {
        using var tempDatabase = new TempDatabase("bounded-sync-window.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetSessionsAsync();

        var boundaryTrack = new Track
        {
            Id = Guid.NewGuid(),
            Points = [new TrackPoint(100, 1, 1, 10)],
            Updated = 5,

        };
        var futureTrack = new Track
        {
            Id = Guid.NewGuid(),
            Points = [new TrackPoint(101, 2, 2, 11)],
            Updated = 6,

        };
        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Insert(boundaryTrack);
            connection.Insert(futureTrack);
        }

        var sinceExclusive = SynchronizationProtocol.GetSinceExclusive(cursor: 5);
        var changed = await database.GetChangedAsync<Track>(sinceExclusive, upperInclusive: 5);

        Assert.Equal(4, sinceExclusive);
        Assert.Equal(boundaryTrack.Id, Assert.Single(changed).Id);
    }

    [Fact]
    public async Task GetSynchronizationDataAsync_DoesNotResendUnchangedTracksForChangedSessions()
    {
        using var tempDatabase = new TempDatabase("sync-data.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
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

            });

            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                Updated = 150,

            });
        }

        var syncData = await database.GetSynchronizationDataAsync(100, 150);

        Assert.Equal(150, syncData.UpperBound);
        Assert.Single(syncData.Sessions);
        Assert.Empty(syncData.Tracks);

    }

    [Fact]
    public async Task MergeAllAsync_AcceptsPushedSegmentOfPushedSession()
    {
        using var deviceDatabase = new TempDatabase("segment-device.db");
        using var hubDatabase = new TempDatabase("segment-hub.db");
        var device = new TestPersistenceHarness(deviceDatabase.DatabasePath);
        var hub = new TestPersistenceHarness(hubDatabase.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", string.Empty, null);
        await device.PutSessionAsync(session);
        var segment = new SessionSegment { OwnerKey = "source-hash", Name = "climb", StartSeconds = 1, EndSeconds = 4 };
        await device.SessionSegmentRepository.InsertAsync(segment);

        await hub.MergeAllAsync(await device.GetSynchronizationDataAsync(0, PersistenceTestData.CreateTimeProvider().GetUtcNow().ToUnixTimeSeconds()));

        var merged = Assert.Single(await hub.SessionSegmentRepository.GetLiveAsync());
        Assert.Equal((segment.Id, "source-hash", "climb"), (merged.Id, merged.OwnerKey, merged.Name));
    }

    [Fact]
    public async Task GetSynchronizationDataAsync_DoesNotLeakFutureRelatedTrackVersion()
    {
        using var tempDatabase = new TempDatabase("bounded-related-track.db");
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Insert(new Track
            {
                Id = trackId,
                Points = [new TrackPoint(100, 1, 1, 10)],
                Updated = 151,

            });
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                Updated = 150,

            });
        }

        var syncData = await database.GetSynchronizationDataAsync(100, 150);

        Assert.Single(syncData.Sessions);
        Assert.Empty(syncData.Tracks);
    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_AllowsDeletedTrackWithoutPoints()
    {
        using var tempDatabase = new TempDatabase("deleted-track-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);

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

                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        Assert.Empty(verificationConnection.Table<Track>().Where(track => track.Id == trackId));
        Assert.Equal(100, verificationConnection.ExecuteScalar<long>(
            "SELECT deleted FROM sync_deletion WHERE entity_table='track' AND id=?", trackId));

    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_RetainsHeldProcessedGeneration()
    {
        using var tempDatabase = new TempDatabase("remote-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        byte[] originalPsst = [1, 2, 3, 4];

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 1234)
            {
                ProcessedData = originalPsst,
                Updated = 1,

            });
        }

        var remoteSession = new Session(sessionId, "remote", "remote desc", setupId, 1234)
        {
            GpsOffsetSeconds = 1.25,
            ProcessingFingerprintJson = """{"remote":true}""",
            DurationSeconds = 65,
            DistanceMeters = 10,
            AscentMeters = 4,
            DescentMeters = 2,
            FrontSpringRate = "50",
            RearSpringRate = "60",
            Updated = 99,

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

        };

        var beforeMerge = await database.GetSessionAsync(sessionId);

        await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
        {
            Sessions = [remoteSession],
            Tracks = [remoteTrack]
        });

        var session = await database.GetSessionAsync(sessionId);

        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);
        var fullTrack = await database.GetAsync<Track>(trackId);

        Assert.NotNull(session);
        Assert.Equal("remote", session!.Name);
        Assert.Equal("remote desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);

        Assert.Equal(1.25, session.GpsOffsetSeconds);
        // The held-BLOB row retains every processed-generation field until the
        // matching BLOB arrives. Independent session metadata still syncs now.
        Assert.Equal(beforeMerge.ProcessingFingerprintJson, session.ProcessingFingerprintJson);
        Assert.Equal(beforeMerge.DurationSeconds, session.DurationSeconds);
        Assert.Equal(beforeMerge.DistanceMeters, session.DistanceMeters);
        Assert.Equal(beforeMerge.AscentMeters, session.AscentMeters);
        Assert.Equal(beforeMerge.DescentMeters, session.DescentMeters);
        Assert.Equal(99, session.Updated);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.Equal(originalPsst, rawPsst);
        Assert.Equal(beforeMerge.ProcessedTelemetryRevision, session.ProcessedTelemetryRevision);

        Assert.NotNull(fullTrack);
        Assert.Equal(2, fullTrack!.Points.Count);
        Assert.Equal(1, fullTrack.PointsRevision);

    }

    [Fact]
    public async Task MergeAllAsync_RetainsHeldProcessedGeneration()
    {
        using var tempDatabase = new TempDatabase("merge-session-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        byte[] originalPsst = [1, 2, 3, 4];

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 1234)
            {
                ProcessedData = originalPsst,
                Updated = 1,

            });
        }

        var beforeMerge = await database.GetSessionAsync(sessionId);

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "remote", "remote desc", setupId, 1234)
                {
                    GpsOffsetSeconds = 1.25,
                    ProcessingFingerprintJson = """{"remote":true}""",
                    DurationSeconds = 65,
                    DistanceMeters = 10,
                    AscentMeters = 4,
                    DescentMeters = 2,
                    FrontSpringRate = "50",
                    RearSpringRate = "60",
                    Updated = 99,

                }
            ]
        });

        var session = await database.GetSessionAsync(sessionId);
        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0, long.MaxValue));

        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal("remote", session!.Name);
        Assert.Equal("remote desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);

        Assert.Equal(1.25, session.GpsOffsetSeconds);
        Assert.Equal(beforeMerge.ProcessingFingerprintJson, session.ProcessingFingerprintJson);
        Assert.Equal(beforeMerge.DurationSeconds, session.DurationSeconds);
        Assert.Equal(beforeMerge.DistanceMeters, session.DistanceMeters);
        Assert.Equal(beforeMerge.AscentMeters, session.AscentMeters);
        Assert.Equal(beforeMerge.DescentMeters, session.DescentMeters);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.True(session.HasProcessedData);
        Assert.True(changedSession.HasProcessedData);
        Assert.Equal(originalPsst, rawPsst);

    }

    [Theory]
    [InlineData(PushSwapScenario.CurrentInputs, true, false, false)]
    [InlineData(PushSwapScenario.CurrentInputs, true, false, true)]
    [InlineData(PushSwapScenario.DerivationWindowChanged, true, false, false)]
    [InlineData(PushSwapScenario.DerivationWindowChanged, true, false, true)]
    [InlineData(PushSwapScenario.MetadataBeforeSource, true, true, false)]
    [InlineData(PushSwapScenario.MetadataBeforeSource, true, true, true)]
    [InlineData(PushSwapScenario.StaleIncoming, false, false, false)]
    [InlineData(PushSwapScenario.StaleIncoming, false, false, true)]
    [InlineData(PushSwapScenario.DerivedStaleIncoming, false, false, false)]
    [InlineData(PushSwapScenario.DerivedStaleIncoming, false, false, true)]
    [InlineData(PushSwapScenario.Equivalent, false, false, false)]
    [InlineData(PushSwapScenario.Equivalent, false, false, true)]
    [InlineData(PushSwapScenario.LegacyHeld, true, false, false)]
    [InlineData(PushSwapScenario.LegacyHeld, true, false, true)]
    [InlineData(PushSwapScenario.HeldCurrent, false, false, false)]
    [InlineData(PushSwapScenario.HeldCurrent, false, false, true)]
    public async Task MergeDirections_RequestOnlyCurrentOrSourcePendingGenerations(
        PushSwapScenario scenario,
        bool expectSwapRequest,
        bool expectRecordedSourceMissing,
        bool pull)
    {
        using var tempDatabase = new TempDatabase("push-swap-eligibility.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var targetTrackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();
        var (heldFingerprint, incomingFingerprint, window) = await SeedPushSwapScenarioAsync(
            database,
            sessionId,
            setupId,
            bikeId,
            scenario);

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(WithWindow(new Session(sessionId, "hub", "desc", setupId, 100)
            {
                ProcessedData = [1, 2, 3],
                ProcessingFingerprintJson = heldFingerprint,
                Updated = 1,

            }, window.Held));
        }

        var data = new SynchronizationData
        {
            Sessions =
            [
                WithWindow(new Session(sessionId, "hub", "desc", setupId, 100)
                {
                    ProcessingFingerprintJson = incomingFingerprint,
                    DurationSeconds = 80,
                    DistanceMeters = 20,
                    AscentMeters = 8,
                    DescentMeters = 3,

                    GpsOffsetSeconds = 1.25,

                    Updated = 99,

                }, window.Incoming)
            ]
        };
        IReadOnlyList<SessionBlobSwap> swaps;
        if (pull) swaps = await database.ApplyRemoteSynchronizationDataAndReturnSwapsAsync(data);
        else
        {
            await database.MergeAllAsync(data);
            var request = await database.GetSessionBlobSwapRequestAsync(sessionId);
            swaps = request is null ? [] : [request];
        }

        using var verify = new SQLiteConnection(databasePath);
        if (expectRecordedSourceMissing)
        {
            Assert.Equal(0, verify.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM session_recording_source WHERE session_id = ?",
                sessionId));
        }

        Assert.Equal(expectSwapRequest ? 1 : 0, swaps.Count);
        Assert.Equal(heldFingerprint, (await database.GetSessionAsync(sessionId))!.ProcessingFingerprintJson);
        if (expectSwapRequest)
        {
            var swap = Assert.Single(swaps);
            Assert.Equal(incomingFingerprint, swap.TargetFingerprint);
            Assert.Equal(80, swap.TargetGeneration.DurationSeconds);
        }
    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_QueuesSwap_WhenDerivedSessionSourceExistsUnderWindowSource()
    {
        using var tempDatabase = new TempDatabase("derived-pull-swap.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var database = new TestPersistenceHarness(databasePath);
        var (localFingerprint, remoteFingerprint, windows) = await SeedPushSwapScenarioAsync(
            database, sessionId, setupId, Guid.NewGuid(), PushSwapScenario.DerivationWindowChanged);
        var window = windows.Incoming;
        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(WithWindow(new Session(sessionId, "local", "desc", setupId, 100)
            {
                ProcessedData = [1, 2, 3],
                ProcessingFingerprintJson = localFingerprint,
                Updated = 1,

            }, window));
        }

        var swaps = await database.ApplyRemoteSynchronizationDataAndReturnSwapsAsync(new SynchronizationData
        {
            Sessions =
            [
                WithWindow(new Session(sessionId, "remote", "desc", setupId, 100)
                {
                    ProcessingFingerprintJson = remoteFingerprint,
                    DurationSeconds = 80,
                    DistanceMeters = 20,
                    AscentMeters = 8,
                    DescentMeters = 3,

                    GpsOffsetSeconds = 1.25,

                    Updated = 99,

                }, window)
            ]
        });

        var swap = Assert.Single(swaps);
        Assert.Equal(sessionId, swap.SessionId);
        Assert.Equal(remoteFingerprint, swap.TargetFingerprint);
        Assert.NotNull(swap.TargetGeneration);
        Assert.Equal(80, swap.TargetGeneration!.DurationSeconds);

    }

    [Fact]
    public async Task PulledSwap_PublishesAlignmentBeforeReplacingHeldGeneration()
    {
        using var tempDatabase = new TempDatabase("pulled-generation-boundary.db");
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var (localFingerprint, remoteFingerprint, _) = await SeedPushSwapScenarioAsync(
            database, sessionId, setupId, Guid.NewGuid(), PushSwapScenario.CurrentInputs, 100);
        var heldData = PersistenceTestData.CreateTelemetryBlob(65);
        var targetData = PersistenceTestData.CreateTelemetryBlob(80);
        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Insert(new Session(sessionId, "local", "desc", setupId, 100)
            {
                ProcessedData = heldData,
                ProcessingFingerprintJson = localFingerprint,
                DurationSeconds = 65,
                DistanceMeters = 10,
                AscentMeters = 4,
                DescentMeters = 2,

                GpsOffsetSeconds = 0.5,

                Updated = 1,

            });
        }
        await database.SeedGpsMetricsAsync(sessionId, new GpsMetrics(10, 4, 2));
        var held = (await database.GetSessionAsync(sessionId))!;

        var swaps = await database.ApplyRemoteSynchronizationDataAndReturnSwapsAsync(
            new SynchronizationData
            {
                Sessions =
                [
                    new Session(sessionId, "remote", "desc", setupId, 100)
                    {
                        ProcessingFingerprintJson = remoteFingerprint,
                        VelocityFilterWindowMilliseconds = 100,
                        DurationSeconds = 80,
                        DistanceMeters = 20,
                        AscentMeters = 8,
                        DescentMeters = 3,

                        GpsOffsetSeconds = 1.25,

                        Updated = 99,

                    },
                ],
            });

        var swap = Assert.Single(swaps);
        var afterMetadata = (await database.GetSessionAsync(sessionId))!;
        Assert.Equal(100, afterMetadata.VelocityFilterWindowMilliseconds);
        Assert.Equal(held.ProcessingFingerprintJson, afterMetadata.ProcessingFingerprintJson);

        Assert.Equal(1.25, afterMetadata.GpsOffsetSeconds);
        Assert.Equal(heldData, await database.SessionRepository.GetSessionRawPsstAsync(
            sessionId,
            held.ProcessedTelemetryRevision));

        await database.SwapSessionPsstAsync(
            sessionId,
            targetData,
            remoteFingerprint,
            swap.TargetGeneration!);

        var target = (await database.GetSessionAsync(sessionId))!;
        Assert.Equal(99, target.Updated);
        Assert.Equal(remoteFingerprint, target.ProcessingFingerprintJson);
        Assert.Equal(80, target.DurationSeconds);
        Assert.Equal(10, target.DistanceMeters);
        Assert.Equal(4, target.AscentMeters);
        Assert.Equal(2, target.DescentMeters);

        Assert.Equal(1.25, target.GpsOffsetSeconds);
        Assert.Null(await database.SessionRepository.GetSessionRawPsstAsync(
            sessionId,
            held.ProcessedTelemetryRevision));

        Assert.Equal(targetData, await database.SessionRepository.GetSessionRawPsstAsync(
            sessionId,
            target.ProcessedTelemetryRevision));

    }

    [Fact]
    public async Task MergeAllAsync_PushPayloadRoundTrip_PreservesExistingPsst_WhenSyncStopsBeforeRepair()
    {
        using var tempDatabase = new TempDatabase("interrupted-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        byte[] originalPsst = [9, 8, 7, 6];

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "server", "server desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,

            });
        }

        var pushPayload = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "client", "client desc", null, 1234)
                {
                    ProcessedData = [1, 2, 3],
                    Updated = 99,

                }
            ]
        }));

        Assert.NotNull(pushPayload);
        Assert.Single(pushPayload!.Sessions);
        Assert.Null(pushPayload.Sessions[0].ProcessedData);
        Assert.False(pushPayload.Sessions[0].HasProcessedData);

        await database.MergeAllAsync(pushPayload);

        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(sessionId));
        Assert.Empty(await database.GetIncompleteSessionIdsAsync());

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotMutateIncomingBoardTimestamps_WhenInsertOrUpdateAccepted()
    {
        using var tempDatabase = new TempDatabase("merge-board-nonmutating.db");
        var databasePath = tempDatabase.DatabasePath;
        var existingBoardId = Guid.NewGuid();
        var insertedBoardId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(existingBoardId, null)
            {
                Updated = 1,

            });
        }

        var updatedBoard = new Board(existingBoardId, Guid.NewGuid())
        {
            Updated = 99,

        };
        var insertedBoard = new Board(insertedBoardId, Guid.NewGuid())
        {
            Updated = 199,

        };

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards = [updatedBoard, insertedBoard]
        });

        Assert.Equal(99, updatedBoard.Updated);

        Assert.Equal(199, insertedBoard.Updated);


        var storedUpdatedBoard = await database.GetAsync<Board>(existingBoardId);
        var storedInsertedBoard = await database.GetAsync<Board>(insertedBoardId);

        Assert.NotNull(storedUpdatedBoard);
        Assert.NotNull(storedInsertedBoard);
        Assert.Equal(99, storedUpdatedBoard!.Updated);
        Assert.Equal(199, storedInsertedBoard!.Updated);

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotMutateIncomingSessionTimestamps_WhenInsertOrUpdateAccepted()
    {
        using var tempDatabase = new TempDatabase("merge-session-nonmutating.db");
        var databasePath = tempDatabase.DatabasePath;
        var existingSessionId = Guid.NewGuid();
        var insertedSessionId = Guid.NewGuid();
        byte[] originalPsst = [4, 3, 2, 1];

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(existingSessionId, "local", "local desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,

            });
        }

        var updatedSession = new Session(existingSessionId, "remote", "remote desc", null, 1234)
        {
            Updated = 99,

        };
        var insertedSession = new Session(insertedSessionId, "new", "new desc", null, 2345)
        {
            Updated = 199,

        };

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions = [updatedSession, insertedSession]
        });

        Assert.Equal(99, updatedSession.Updated);

        Assert.Equal(199, insertedSession.Updated);


        using var verificationConnection = new SQLiteConnection(databasePath);
        var storedUpdatedSession = verificationConnection.Table<Session>().Single(s => s.Id == existingSessionId);
        var storedInsertedSession = verificationConnection.Table<Session>().Single(s => s.Id == insertedSessionId);

        Assert.NotNull(storedUpdatedSession);
        Assert.NotNull(storedInsertedSession);
        Assert.Equal(99, storedUpdatedSession.Updated);
        Assert.Equal(199, storedInsertedSession.Updated);
        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(existingSessionId));
        Assert.Null(await database.GetSessionRawPsstAsync(insertedSessionId));

    }

    private static async Task<(string HeldFingerprint, string IncomingFingerprint, (RecordedSessionDerivationWindow? Held, RecordedSessionDerivationWindow? Incoming) Window)> SeedPushSwapScenarioAsync(
        TestPersistenceHarness database,
        Guid sessionId,
        Guid setupId,
        Guid bikeId,
        PushSwapScenario scenario, int? velocityWindow = null)
    {
        var setup = new Setup { Id = setupId, BikeId = bikeId, Name = "setup" };
        var bike = new Bike { Id = bikeId, Name = "bike", HeadAngle = 65 };
        await database.PutAsync(setup);
        await database.PutAsync(bike);

        var fingerprintService = new ProcessingFingerprintService();
        var session = new Session(sessionId, "session", "desc", setupId, 100) { VelocityFilterWindowMilliseconds = velocityWindow };
        var setupSnapshot = SetupSnapshot.From(setup);
        var bikeSnapshot = BikeSnapshot.From(bike);

        if (scenario == PushSwapScenario.DerivationWindowChanged)
        {
            var oldSourceSessionId = Guid.NewGuid();
            var newSourceSessionId = Guid.NewGuid();
            var oldSource = PersistenceTestData.CreateRecordedSessionSource(oldSourceSessionId);
            var newSource = PersistenceTestData.CreateRecordedSessionSource(newSourceSessionId);
            await database.PutRecordedSessionSourceAsync(oldSource);
            await database.PutRecordedSessionSourceAsync(newSource);
            var oldWindow = new RecordedSessionDerivationWindow(oldSourceSessionId, 1, null);
            var newWindow = new RecordedSessionDerivationWindow(newSourceSessionId, 1, null);

            return (
                AppJson.Serialize(fingerprintService.CreateCurrent(
                    SessionSnapshot.From(WithWindow(session, oldWindow)),
                    setupSnapshot,
                    bikeSnapshot,
                    RecordedSessionSourceSnapshot.From(oldSource))),
                AppJson.Serialize(fingerprintService.CreateCurrent(
                    SessionSnapshot.From(WithWindow(session, newWindow)),
                    setupSnapshot,
                    bikeSnapshot,
                    RecordedSessionSourceSnapshot.From(newSource))),
                (oldWindow, newWindow));
        }

        var sourceSessionId = scenario == PushSwapScenario.DerivedStaleIncoming
            ? Guid.NewGuid()
            : sessionId;
        var source = PersistenceTestData.CreateRecordedSessionSource(sourceSessionId);
        if (scenario != PushSwapScenario.MetadataBeforeSource)
        {
            await database.PutRecordedSessionSourceAsync(source);
        }

        var derivationWindow = scenario == PushSwapScenario.DerivedStaleIncoming
            ? new RecordedSessionDerivationWindow(sourceSessionId, 1, null)
            : null;
        var canonical = fingerprintService.CreateCurrent(
            SessionSnapshot.From(WithWindow(session, derivationWindow)),
            setupSnapshot,
            bikeSnapshot,
            RecordedSessionSourceSnapshot.From(source));

        if (scenario == PushSwapScenario.Equivalent)
            return (AppJson.Serialize(canonical), AppJson.Serialize(canonical), (derivationWindow, derivationWindow));
        if (scenario == PushSwapScenario.LegacyHeld)
            return ("{}", AppJson.Serialize(canonical), (derivationWindow, derivationWindow));
        if (scenario == PushSwapScenario.HeldCurrent)
            return (AppJson.Serialize(canonical), AppJson.Serialize(canonical with { DependencyHash = "stale" }), (derivationWindow, derivationWindow));

        return scenario is PushSwapScenario.StaleIncoming or PushSwapScenario.DerivedStaleIncoming
            ? (
                AppJson.Serialize(canonical with { DependencyHash = "hub-old-hash" }),
                AppJson.Serialize(canonical with { DependencyHash = "client-stale-hash" }),
                (derivationWindow, derivationWindow))
            : (
                AppJson.Serialize(canonical with { DependencyHash = "stale-dependency-hash" }),
                AppJson.Serialize(canonical),
                (derivationWindow, derivationWindow));
    }

    private static Session WithWindow(Session session, RecordedSessionDerivationWindow? window)
    {
        session.SourceSessionId = window?.SourceSessionId;
        session.WindowStartSeconds = window?.StartSeconds ?? 0;
        session.WindowEndSeconds = window?.EndSeconds;
        return session;
    }

    public enum PushSwapScenario
    {
        CurrentInputs,
        DerivationWindowChanged,
        MetadataBeforeSource,
        StaleIncoming,
        DerivedStaleIncoming,
        Equivalent,
        LegacyHeld,
        HeldCurrent
    }

}
