using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqSessionStateTests
{
    [Fact]
    public void CreateSnapshot_ProjectsAcceptedSessionAndLatestSensorValues()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 700);

        state.ApplySharedSessionState(sessionHeader, LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps);
        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 2),
            new LiveBatchHeader(700, 0, 10, 123456789, 2),
            [new LiveTravelRecord(100, 200), new LiveTravelRecord(111, 222)]));
        state.ApplyFrame(new LiveImuBatchFrame(
            CreateHeader(LiveV2FrameType.ImuBatch, 3),
            new LiveBatchHeader(700, 0, 20, 123456789, 2),
            [
                new ImuRecord(1, 2, 3, 4, 5, 6),
                new ImuRecord(7, 8, 9, 10, 11, 12),
                new ImuRecord(13, 14, 15, 16, 17, 18),
                new ImuRecord(19, 20, 21, 22, 23, 24)
            ]));
        state.ApplyFrame(new LiveGpsBatchFrame(
            CreateHeader(LiveV2FrameType.GpsBatch, 4),
            new LiveBatchHeader(700, 0, 30, 123456789, 1),
            [new GpsRecord(new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc), 48.2, 16.3, 200f, 8f, 90f, 2, 9, 1.5f, 2.5f)]));
        state.ApplyFrame(new LiveSessionStatsFrame(
            CreateHeader(LiveV2FrameType.SessionStats, 5),
            new LiveSessionStats(700, 5, 4, 1, 3, 2, 0)));

        var snapshot = state.CreateSnapshot(LiveConnectionState.Connected, null);

        Assert.Equal(LiveConnectionState.Connected, snapshot.ConnectionState);
        Assert.Equal((uint)700, snapshot.Session.SessionId);
        Assert.Equal(sessionHeader.RequestedSensorMask, snapshot.Session.RequestedSensorMask);
        Assert.Equal(sessionHeader.AcceptedSensorMask, snapshot.Session.AcceptedSensorMask);
        Assert.Equal((uint)200, snapshot.Session.AcceptedTravelHz);
        Assert.True(snapshot.Travel.IsActive);
        Assert.True(snapshot.Travel.FrontIsActive);
        Assert.True(snapshot.Travel.RearIsActive);
        Assert.True(snapshot.Travel.HasData);
        Assert.Equal((ushort)111, snapshot.Travel.FrontMeasurement);
        Assert.Equal((ushort)222, snapshot.Travel.RearMeasurement);
        Assert.Equal((uint)5, snapshot.Travel.QueueDepth);
        Assert.Equal((uint)3, snapshot.Travel.DroppedBatches);

        Assert.Equal(2, snapshot.Imus.Count);
        Assert.Equal(LiveImuLocation.Frame, snapshot.Imus[0].Location);
        Assert.True(snapshot.Imus[0].HasData);
        Assert.Equal((short)13, snapshot.Imus[0].Ax);
        Assert.Equal(LiveImuLocation.Rear, snapshot.Imus[1].Location);
        Assert.Equal((short)19, snapshot.Imus[1].Ax);
        Assert.Equal((uint)4, snapshot.Imus[0].QueueDepth);
        Assert.Equal((uint)2, snapshot.Imus[0].DroppedBatches);

        Assert.True(snapshot.Gps.IsActive);
        Assert.True(snapshot.Gps.HasData);
        Assert.True(snapshot.Gps.PreviewState.IsReady);
        Assert.Equal(48.2, snapshot.Gps.Latitude);
        Assert.Equal(16.3, snapshot.Gps.Longitude);
        Assert.Equal((byte)9, snapshot.Gps.Satellites);
        Assert.Equal((uint)1, snapshot.Gps.QueueDepth);
    }

    [Fact]
    public void CreateSnapshot_HidesInactiveTravelChannel_WhenOnlyOneTravelSensorStarted()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: 702,
            requestedSensorMask: LiveSensorInstanceMask.Travel,
            acceptedSensorMask: LiveSensorInstanceMask.ForkTravel);

        state.ApplySharedSessionState(sessionHeader, LiveStreamMask.Travel);
        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 2),
            new LiveBatchHeader(702, 0, 10, 123456789, 1),
            [new LiveTravelRecord(111, 0)]));

        var snapshot = state.CreateSnapshot(LiveConnectionState.Connected, null);

        Assert.True(snapshot.Travel.IsActive);
        Assert.True(snapshot.Travel.FrontIsActive);
        Assert.False(snapshot.Travel.RearIsActive);
        Assert.True(snapshot.Travel.HasData);
        Assert.Equal((ushort)111, snapshot.Travel.FrontMeasurement);
        Assert.Null(snapshot.Travel.RearMeasurement);
        Assert.Equal(LiveSensorInstanceMask.ShockTravel, snapshot.Session.MissingSensorMask);
    }

    [Fact]
    public void CreateSnapshot_TravelSampleDelay_IsNull_UntilTravelBatchArrives()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 800);
        state.ApplySharedSessionState(sessionHeader, LiveStreamMask.Travel | LiveStreamMask.Imu);

        var snapshotInitial = state.CreateSnapshot(LiveConnectionState.Connected, null);
        Assert.Null(snapshotInitial.Travel.SampleDelay);

        state.ApplyFrame(new LiveImuBatchFrame(
            CreateHeader(LiveV2FrameType.ImuBatch, 2),
            new LiveBatchHeader(800, 0, 10, 123456789, 1),
            [
                new ImuRecord(1, 2, 3, 4, 5, 6),
                new ImuRecord(7, 8, 9, 10, 11, 12)
            ]));
        state.ApplyFrame(new LiveSessionStatsFrame(
            CreateHeader(LiveV2FrameType.SessionStats, 3),
            new LiveSessionStats(800, 5, 4, 1, 3, 2, 0)));

        var snapshotWithoutTravel = state.CreateSnapshot(LiveConnectionState.Connected, null);
        Assert.Null(snapshotWithoutTravel.Travel.SampleDelay);

        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 4),
            new LiveBatchHeader(800, 0, 20, 123456789, 1),
            [new LiveTravelRecord(100, 200)]));

        var snapshotWithTravel = state.CreateSnapshot(LiveConnectionState.Connected, null);
        Assert.NotNull(snapshotWithTravel.Travel.SampleDelay);
        Assert.True(snapshotWithTravel.Travel.SampleDelay >= TimeSpan.Zero);
        Assert.True(snapshotWithTravel.Travel.SampleDelay < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateSnapshot_ImuSampleDelay_IsNull_UntilImuBatchArrives()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 801);
        state.ApplySharedSessionState(sessionHeader, LiveStreamMask.Travel | LiveStreamMask.Imu);

        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 2),
            new LiveBatchHeader(801, 0, 10, 123456789, 1),
            [new LiveTravelRecord(100, 200)]));

        var snapshotWithoutImu = state.CreateSnapshot(LiveConnectionState.Connected, null);
        Assert.All(snapshotWithoutImu.Imus, imu => Assert.Null(imu.SampleDelay));

        state.ApplyFrame(new LiveImuBatchFrame(
            CreateHeader(LiveV2FrameType.ImuBatch, 3),
            new LiveBatchHeader(801, 0, 20, 123456789, 1),
            [
                new ImuRecord(1, 2, 3, 4, 5, 6),
                new ImuRecord(7, 8, 9, 10, 11, 12)
            ]));

        var snapshotWithImu = state.CreateSnapshot(LiveConnectionState.Connected, null);
        Assert.All(snapshotWithImu.Imus, imu =>
        {
            Assert.NotNull(imu.SampleDelay);
            Assert.True(imu.SampleDelay >= TimeSpan.Zero);
            Assert.True(imu.SampleDelay < TimeSpan.FromSeconds(5));
        });
    }

    [Fact]
    public void CreateSnapshot_FormatsDecimalRates_AndAppliesV3StatusCounters()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: 802,
            acceptedTravelRateMhz: 123_450,
            acceptedImuRateMhz: 100_000,
            acceptedGpsRateMhz: 5_500,
            protocolVersion: LiveProtocolVersion.V3);
        state.ApplySharedSessionState(sessionHeader, LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps);

        state.ApplyFrame(new LiveStatusFrame(
            new LiveFrameMetadata(10),
            [
                new LiveStreamStatus(LiveStreamMask.Travel, 1, 0, 0, 2, 20, 3, 30),
                new LiveStreamStatus(LiveStreamMask.Imu, 1, 0, 0, 4, 40, 5, 50),
                new LiveStreamStatus(LiveStreamMask.Gps, 1, 0, 0, 6, 60, 7, 70),
            ]));
        state.ApplyFrame(new LiveBatteryBatchFrame(
            new LiveFrameMetadata(11),
            new LiveBatchHeader(802, 1, 0, 123456789, 1),
            [new LiveBatteryRecord(0, 0, 4100, 0)]));

        var snapshot = state.CreateSnapshot(LiveConnectionState.Connected, null);

        Assert.Equal("Travel: 123.45 Hz", snapshot.Session.AcceptedTravelRateText);
        Assert.Equal("IMU: 100 Hz", snapshot.Session.AcceptedImuRateText);
        Assert.Equal("GPS: 5.5 Hz", snapshot.Session.AcceptedGpsRateText);
        Assert.Equal((uint)5, snapshot.Travel.DroppedBatches);
        Assert.Equal((uint)9, snapshot.Imus[0].DroppedBatches);
        Assert.Equal((uint)13, snapshot.Gps.DroppedBatches);
        Assert.NotNull(snapshot.LastFrameReceivedUtc);
    }

    [Fact]
    public void Reset_ClearsAcceptedSessionAndLatestData()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 701);
        state.ApplySharedSessionState(sessionHeader, LiveStreamMask.Travel);
        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 2),
            new LiveBatchHeader(701, 0, 10, 123456789, 1),
            [new LiveTravelRecord(100, 200)]));

        state.Reset();

        var snapshot = state.CreateSnapshot(LiveConnectionState.Disconnected, null);

        Assert.Null(snapshot.Session.SessionId);
        Assert.False(snapshot.Travel.HasData);
        Assert.Empty(snapshot.Imus);
        Assert.False(snapshot.Gps.HasData);
    }

    private static LiveFrameMetadata CreateHeader(LiveV2FrameType frameType, uint sequence) => new(sequence);
}
