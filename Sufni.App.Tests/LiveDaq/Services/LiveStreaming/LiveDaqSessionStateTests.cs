using Microsoft.Extensions.Time.Testing;
using Sufni.Telemetry;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqSessionStateTests
{
    [Fact]
    public void CreateSnapshot_ProjectsAcceptedSessionAndLatestSensorValues()
    {
        var time = new FakeTimeProvider();
        var state = new LiveDaqSessionState(time);
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 700);

        state.ApplySharedSessionState(sessionHeader);
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

        var snapshot = state.CreateSnapshot(new LiveConnectionState.Connected());

        Assert.Equal(new LiveConnectionState.Connected(), snapshot.ConnectionState);
        Assert.Equal((uint)700, snapshot.Session.SessionId);
        Assert.Equal((uint)200_000, snapshot.Session.AcceptedTravelRateMhz);
        Assert.True(snapshot.Travel.FrontIsActive);
        Assert.True(snapshot.Travel.RearIsActive);
        Assert.Equal((ushort)111, snapshot.Travel.FrontMeasurement);
        Assert.Equal((ushort)222, snapshot.Travel.RearMeasurement);
        Assert.Equal((uint)5, snapshot.Travel.QueueDepth);

        Assert.Equal(2, snapshot.Imus.Count);
        Assert.Equal(LiveImuLocation.Frame, snapshot.Imus[0].Location);
        Assert.Equal((short)13, snapshot.Imus[0].Ax);
        Assert.Equal(LiveImuLocation.Rear, snapshot.Imus[1].Location);
        Assert.Equal((short)19, snapshot.Imus[1].Ax);
        Assert.Equal((uint)4, snapshot.Imus[0].QueueDepth);

        Assert.Equal(48.2, snapshot.Gps.Latitude);
        Assert.Equal(16.3, snapshot.Gps.Longitude);
        Assert.Equal((uint)1, snapshot.Gps.QueueDepth);
    }

    [Fact]
    public void CreateSnapshot_HidesInactiveTravelChannel_WhenOnlyOneTravelSensorStarted()
    {
        var time = new FakeTimeProvider();
        var state = new LiveDaqSessionState(time);
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: 702,
            requestedSensorMask: LiveSensorInstanceMask.Travel,
            acceptedSensorMask: LiveSensorInstanceMask.ForkTravel);

        state.ApplySharedSessionState(sessionHeader);
        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 2),
            new LiveBatchHeader(702, 0, 10, 123456789, 1),
            [new LiveTravelRecord(111, 0)]));

        var snapshot = state.CreateSnapshot(new LiveConnectionState.Connected());

        Assert.True(snapshot.Travel.FrontIsActive);
        Assert.False(snapshot.Travel.RearIsActive);
        Assert.Equal((ushort)111, snapshot.Travel.FrontMeasurement);
        Assert.Null(snapshot.Travel.RearMeasurement);
    }

    [Fact]
    public void CreateSnapshot_TravelSampleDelay_IsNull_UntilTravelBatchArrives()
    {
        var time = new FakeTimeProvider();
        var state = new LiveDaqSessionState(time);
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 800);
        state.ApplySharedSessionState(sessionHeader);

        var snapshotInitial = state.CreateSnapshot(new LiveConnectionState.Connected());
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

        var snapshotWithoutTravel = state.CreateSnapshot(new LiveConnectionState.Connected());
        Assert.Null(snapshotWithoutTravel.Travel.SampleDelay);

        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 4),
            new LiveBatchHeader(800, 0, 20, 123456789, 1),
            [new LiveTravelRecord(100, 200)]));

        time.Advance(TimeSpan.FromMilliseconds(750));
        var snapshotWithTravel = state.CreateSnapshot(new LiveConnectionState.Connected());
        Assert.Equal(TimeSpan.FromMilliseconds(750), snapshotWithTravel.Travel.SampleDelay);
    }

    [Fact]
    public void CreateSnapshot_ImuSampleDelay_IsNull_UntilImuBatchArrives()
    {
        var time = new FakeTimeProvider();
        var state = new LiveDaqSessionState(time);
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 801);
        state.ApplySharedSessionState(sessionHeader);

        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 2),
            new LiveBatchHeader(801, 0, 10, 123456789, 1),
            [new LiveTravelRecord(100, 200)]));

        var snapshotWithoutImu = state.CreateSnapshot(new LiveConnectionState.Connected());
        Assert.All(snapshotWithoutImu.Imus, imu => Assert.Null(imu.SampleDelay));

        state.ApplyFrame(new LiveImuBatchFrame(
            CreateHeader(LiveV2FrameType.ImuBatch, 3),
            new LiveBatchHeader(801, 0, 20, 123456789, 1),
            [
                new ImuRecord(1, 2, 3, 4, 5, 6),
                new ImuRecord(7, 8, 9, 10, 11, 12)
            ]));

        time.Advance(TimeSpan.FromMilliseconds(250));
        var snapshotWithImu = state.CreateSnapshot(new LiveConnectionState.Connected());
        Assert.All(snapshotWithImu.Imus, imu => Assert.Equal(TimeSpan.FromMilliseconds(250), imu.SampleDelay));
    }

    [Fact]
    public void CreateSnapshot_FormatsDecimalRates()
    {
        var time = new FakeTimeProvider();
        var state = new LiveDaqSessionState(time);
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: 802,
            acceptedTravelRateMhz: 123_450,
            acceptedImuRateMhz: 100_000,
            acceptedGpsRateMhz: 5_500,
            expectsFinalStatus: true);
        state.ApplySharedSessionState(sessionHeader);

        var snapshot = state.CreateSnapshot(new LiveConnectionState.Connected());

        Assert.Equal("Travel: 123.45 Hz", snapshot.Session.AcceptedTravelRateText);
        Assert.Equal("IMU: 100 Hz", snapshot.Session.AcceptedImuRateText);
        Assert.Equal("GPS: 5.5 Hz", snapshot.Session.AcceptedGpsRateText);
    }

    [Fact]
    public void Reset_ClearsAcceptedSessionAndLatestData()
    {
        var time = new FakeTimeProvider();
        var state = new LiveDaqSessionState(time);
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 701);
        state.ApplySharedSessionState(sessionHeader);
        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveV2FrameType.TravelBatch, 2),
            new LiveBatchHeader(701, 0, 10, 123456789, 1),
            [new LiveTravelRecord(100, 200)]));

        state.Reset();

        var snapshot = state.CreateSnapshot(new LiveConnectionState.Disconnected());

        Assert.Null(snapshot.Session.SessionId);
        Assert.Null(snapshot.Travel.FrontMeasurement);
        Assert.Null(snapshot.Travel.RearMeasurement);
        Assert.Empty(snapshot.Imus);
        Assert.Null(snapshot.Gps.Latitude);
        Assert.Null(snapshot.Gps.Longitude);
    }

    private static LiveFrameMetadata CreateHeader(LiveV2FrameType frameType, uint sequence) => new(sequence);
}
