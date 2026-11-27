using NSubstitute;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.SensorFindings.ViewModels;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.SensorFindings;

public sealed class SensorFindingsViewModelTests : IDisposable
{
    private static readonly SensorIdentity Shock = new(SensorKind.ShockTravel);
    private static readonly SensorIdentity Gps = new(SensorKind.Gps);
    private static readonly SensorFinding FrozenShock = new(Shock, SensorFaultReason.Frozen, [new(10, 20)]);
    private static readonly SensorFinding OutOfRangeShock = new(Shock, SensorFaultReason.OutOfRange, [new(30, 40)]);
    private static readonly SensorFinding GpsGlitch = new(Gps, SensorFaultReason.Glitch, [new(5, 6)]);
    private static readonly SensorFinding GpsFixLost = new(Gps, SensorFaultReason.FixLost, [new(50, 60)]);

    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly SessionStore store;
    private readonly ISessionCoordinator coordinator = TestCoordinatorSubstitutes.Session();
    private readonly List<FailurePresentation> errors = [];
    private readonly List<string> notifications = [];
    private readonly SessionSnapshot session = TestSnapshots.Session();

    public SensorFindingsViewModelTests() => store = new SessionStore(publication);

    public void Dispose()
    {
        store.Dispose();
        publication.Dispose();
    }

    private Task<SensorFindingsViewModel> CreateAsync(SensorDecisions decisions, params SensorFinding[] findings) =>
        CreateAsync(session with { SensorDecisions = decisions }, findings);

    private async Task<SensorFindingsViewModel> CreateAsync(SessionSnapshot published, SensorFinding[] findings)
    {
        await store.PublishSessionsChangedAsync([published]);
        return new SensorFindingsViewModel(session.Id, [.. findings], store, coordinator, new InlineUiThreadDispatcher(),
            errors.Add, notifications.Add);
    }

    [Fact]
    public async Task Rows_ShowUndecidedFindingsPerSensorAndClass_WithActionsOnlyForDroppableFindings()
    {
        var keptOutOfRange = SensorDecisions.None.With(Shock, SensorDecision.Keep, [new(30, 40)]);

        using var findings = await CreateAsync(keptOutOfRange, FrozenShock, OutOfRangeShock, GpsGlitch, GpsFixLost);

        Assert.Equal(
            [(Shock, true, [new SourceTimeRange(10, 20)]), (Gps, true, [new SourceTimeRange(5, 6)]), (Gps, false, [new SourceTimeRange(50, 60)])],
            findings.Rows.Select(row => (row.Sensor, row.CanDecide, row.Ranges.ToArray())).ToArray(), RowComparer.Instance);
        Assert.False(findings.Rows[2].DropCommand.CanExecute(null));
    }

    public static TheoryData<SensorFinding[], double, double, string> Summaries => new()
    {
        { [new(Shock, SensorFaultReason.Frozen, [new(10, 20), new(100, 110)])], 600, 0, "frozen for 20 s (0:10–1:50)" },
        { [new(Shock, SensorFaultReason.Frozen, [new(130, 140)])], 600, 100, "frozen for 10 s (0:30)" },
        { [new(Gps, SensorFaultReason.Glitch, [new(5, 5.4)])], 600, 0, "glitches for under 1 s (0:05)" },
        { [new(Shock, SensorFaultReason.Zero, [new(0, 590)])], 600, 0, "reads zero for the whole ride" },
        { [new(Shock, SensorFaultReason.Frozen, [new(3700, 3790)])], 4000, 0, "frozen for 1 min 30 s (1:01:40–1:03:10)" },
        {
            [new(Shock, SensorFaultReason.Frozen, [new(10, 20)]), new(Shock, SensorFaultReason.OutOfRange, [new(100, 160)])],
            600, 0, "out of range for 1 min (1:40–2:40), frozen for 10 s (0:10)"
        },
    };

    [Theory]
    [MemberData(nameof(Summaries))]
    public async Task Details_SummarizeEachReasonInSessionTime_LongestFirst(SensorFinding[] findings, double duration,
        double windowStart, string expected)
    {
        using var vm = await CreateAsync(session with { DurationSeconds = duration, WindowStartSeconds = windowStart }, findings);

        Assert.Equal(expected, Assert.Single(vm.Rows).Details);
    }

    [Fact]
    public async Task RangesText_ListsTheExactRangesOfEachReason()
    {
        using var vm = await CreateAsync(session with { DurationSeconds = 600 },
            [new(Shock, SensorFaultReason.Frozen, [new(10, 20), new(100, 110)]), new(Shock, SensorFaultReason.OutOfRange, [new(200, 300)])]);

        Assert.Equal($"out of range: 3:20–5:00{Environment.NewLine}frozen: 0:10–0:20, 1:40–1:50", Assert.Single(vm.Rows).RangesText);
    }

    [Fact]
    public async Task PublishedDecision_RemovesTheRowOfThatSensor()
    {
        using var findings = await CreateAsync(SensorDecisions.None, FrozenShock);
        Assert.True(findings.HasRows);

        await store.PublishSessionsChangedAsync([session with
        {
            SensorDecisions = SensorDecisions.None.With(Shock, SensorDecision.Drop, [new(10, 20)]),
        }]);

        Assert.Empty(findings.Rows);
        Assert.False(findings.HasRows);
    }

    [Theory]
    [InlineData(SensorDecision.Drop)]
    [InlineData(SensorDecision.Keep)]
    public async Task Decision_SendsAllUndecidedRangesOfTheSensor(SensorDecision decision)
    {
        coordinator.SetSensorDecisionAsync(default, default, default, default!).ReturnsForAnyArgs(new SensorDecisionResult.Kept());
        using var findings = await CreateAsync(SensorDecisions.None, FrozenShock, OutOfRangeShock);
        var row = Assert.Single(findings.Rows);

        await (decision == SensorDecision.Drop ? row.DropCommand : row.KeepCommand).ExecuteAsync(null);

        await coordinator.Received(1).SetSensorDecisionAsync(session.Id, Shock, decision,
            Arg.Is<IReadOnlyList<SourceTimeRange>>(ranges => ranges.SequenceEqual(new SourceTimeRange[] { new(10, 20), new(30, 40) })));
    }

    public static TheoryData<SensorDecisionResult, bool, bool> Outcomes => new()
    {
        { new SensorDecisionResult.Dropped(new SessionRecomputeResult.Recomputed()), false, false },
        { new SensorDecisionResult.Dropped(new SessionRecomputeResult.ProcessingFailed(SessionProcessingFailureReason.NoTravelData)), true, false },
        { new SensorDecisionResult.Failed(SensorDecisionFailureReason.Save, "diagnostic"), true, false },
        { new SensorDecisionResult.Missing(), false, true },
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task DecisionOutcome_IsReportedOnlyWhenItNeedsAttention(SensorDecisionResult result, bool error, bool notice)
    {
        coordinator.SetSensorDecisionAsync(default, default, default, default!).ReturnsForAnyArgs(result);
        using var findings = await CreateAsync(SensorDecisions.None, FrozenShock);

        await Assert.Single(findings.Rows).DropCommand.ExecuteAsync(null);

        Assert.Equal(error, errors.Count == 1);
        Assert.Equal(notice, notifications.Count == 1);
    }

    private sealed class RowComparer : IEqualityComparer<(SensorIdentity, bool, SourceTimeRange[])>
    {
        public static readonly RowComparer Instance = new();

        public bool Equals((SensorIdentity, bool, SourceTimeRange[]) x, (SensorIdentity, bool, SourceTimeRange[]) y) =>
            x.Item1 == y.Item1 && x.Item2 == y.Item2 && x.Item3.SequenceEqual(y.Item3);

        public int GetHashCode((SensorIdentity, bool, SourceTimeRange[]) row) => row.Item1.GetHashCode();
    }
}
