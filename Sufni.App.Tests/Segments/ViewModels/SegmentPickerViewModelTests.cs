using DynamicData;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Stores;
using Sufni.App.Segments.ViewModels;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentPickerViewModelTests
{
    private const long SessionTimestamp = 1_767_261_600;

    private readonly Guid sessionId = Guid.NewGuid();
    private readonly SourceCache<SessionSegmentSnapshot, Guid> segments = new(segment => segment.Id);
    private readonly ISessionSegmentStore segmentStore = Substitute.For<ISessionSegmentStore>();
    private readonly ISegmentCoordinator segmentCoordinator = Substitute.For<ISegmentCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly List<TelemetryTimeRange?> analysisRanges = [];
    private readonly List<string> errors = [];

    public SegmentPickerViewModelTests()
    {
        segmentStore.Connect().Returns(segments.Connect());
        segmentCoordinator.ValidateName(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>()).Returns((string?)null);
        segmentCoordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new StravaSegmentFetchResult.NotConnected());
    }

    [Fact]
    public void SavedSegments_ListsOnlyTheSessionSegmentsInsideTheTelemetry()
    {
        var inside = AddSegment("Inside", 2, 4);
        AddSegment("Trimmed away", 8, 12);
        AddSegment("Other session", 2, 4, Guid.NewGuid());
        using var picker = CreateLoadedPicker(durationSeconds: 10);

        var row = Assert.Single(picker.SavedSegments);
        Assert.Equal(inside.Id, row.Id);
    }

    [Fact]
    public void SavedSegments_SelectsTheRowWhoseRangeIsTheAnalysisRange()
    {
        var selected = AddSegment("Selected", 2, 4);
        AddSegment("Other", 5, 6);
        using var picker = CreateLoadedPicker(analysisRange: new TelemetryTimeRange(2, 4));

        Assert.Equal([selected.Id], picker.SavedSegments.Where(row => row.IsSelected).Select(row => row.Id));
    }

    [Fact]
    public void SelectSavedSegment_SetsItsRangeAndClosesThePicker()
    {
        AddSegment("Segment", 2, 4);
        using var picker = CreateLoadedPicker();
        picker.OpenCommand.Execute(null);

        picker.SelectSavedSegmentCommand.Execute(picker.SavedSegments[0]);

        Assert.Equal([new TelemetryTimeRange(2, 4)], analysisRanges);
        Assert.False(picker.IsOpen);
    }

    [Fact]
    public void SelectSavedSegment_WhenAlreadySelected_ClearsTheAnalysisRange()
    {
        AddSegment("Segment", 2, 4);
        using var picker = CreateLoadedPicker(analysisRange: new TelemetryTimeRange(2, 4));

        picker.SelectSavedSegmentCommand.Execute(picker.SavedSegments[0]);

        Assert.Equal([null], analysisRanges);
    }

    [Fact]
    public async Task SaveSelectionAsync_CreatesSegmentWithTheConfirmedName()
    {
        segmentCoordinator.NextName(sessionId).Returns("Segment 1");
        segmentCoordinator.CreateAsync(sessionId, "Climb", 2, 4).Returns(new SegmentChangeResult.Applied());
        AnswerNamePrompt(prompt =>
        {
            prompt.Text = " Climb ";
            prompt.ConfirmCommand.Execute(null);
        });
        using var picker = CreateLoadedPicker();

        await picker.SaveSelectionAsync(new TelemetryTimeRange(2, 4));

        await segmentCoordinator.Received(1).CreateAsync(sessionId, "Climb", 2, 4);
    }

    [Fact]
    public async Task SaveSelectionAsync_WhenPromptCancelled_SavesNothing()
    {
        segmentCoordinator.NextName(sessionId).Returns("Segment 1");
        AnswerNamePrompt(prompt => prompt.CancelCommand.Execute(null));
        using var picker = CreateLoadedPicker();

        await picker.SaveSelectionAsync(new TelemetryTimeRange(2, 4));

        await segmentCoordinator.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, default);
    }

    [Fact]
    public async Task SaveSelectionAsync_WhenSaveFails_ReportsError()
    {
        segmentCoordinator.NextName(sessionId).Returns("Segment 1");
        segmentCoordinator.CreateAsync(sessionId, "Segment 1", 2, 4)
            .Returns(new SegmentChangeResult.Failed("Session no longer exists."));
        AnswerNamePrompt(prompt => prompt.ConfirmCommand.Execute(null));
        using var picker = CreateLoadedPicker();

        await picker.SaveSelectionAsync(new TelemetryTimeRange(2, 4));

        Assert.Single(errors);
    }

    [Fact]
    public async Task DeleteSavedSegment_AfterConfirmation_DeletesTheSegment()
    {
        var segment = AddSegment("Segment", 2, 4);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        segmentCoordinator.DeleteAsync(segment.Id).Returns(new SegmentChangeResult.Applied());
        using var picker = CreateLoadedPicker();

        await picker.DeleteSavedSegmentCommand.ExecuteAsync(picker.SavedSegments[0]);

        await segmentCoordinator.Received(1).DeleteAsync(segment.Id);
    }

    [Fact]
    public async Task Open_WhenStravaIsConnected_ListsTheSessionEfforts()
    {
        var effort = new StravaSegmentOption("Rock Garden", 320, new TelemetryTimeRange(3, 5));
        segmentCoordinator.FindStravaSegmentsAsync(SessionTimestamp, 10, Arg.Any<CancellationToken>())
            .Returns(new StravaSegmentFetchResult.Loaded([effort]));
        using var picker = CreateLoadedPicker(durationSeconds: 10);

        await picker.OpenCommand.ExecuteAsync(null);

        var row = Assert.Single(picker.StravaSegments);
        Assert.Equal(effort.AnalysisRange, row.Range);
        Assert.False(picker.IsStravaSegmentsLoading);
    }

    [Fact]
    public async Task Close_CancelsTheStravaLoad()
    {
        var fetchToken = new TaskCompletionSource<CancellationToken>();
        var fetch = new TaskCompletionSource<StravaSegmentFetchResult>();
        segmentCoordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                fetchToken.SetResult(call.ArgAt<CancellationToken>(2));
                return fetch.Task;
            });
        using var picker = CreateLoadedPicker();
        var open = picker.OpenCommand.ExecuteAsync(null);
        var token = await fetchToken.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        picker.CloseCommand.Execute(null);
        fetch.SetCanceled(token);
        await open.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.True(token.IsCancellationRequested);
        Assert.False(picker.IsStravaSegmentsLoading);
        Assert.Empty(picker.StravaSegments);
    }

    [Fact]
    public async Task ResetStravaCredentials_DisconnectsAndDropsTheEfforts()
    {
        segmentCoordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new StravaSegmentFetchResult.Loaded([new StravaSegmentOption("Rock Garden", 320, new TelemetryTimeRange(3, 5))]));
        using var picker = CreateLoadedPicker();
        await picker.OpenCommand.ExecuteAsync(null);

        await picker.ResetStravaCredentialsCommand.ExecuteAsync(null);

        await segmentCoordinator.Received(1).ResetStravaAsync();
        Assert.False(picker.IsStravaConnected);
        Assert.Empty(picker.StravaSegments);
    }

    [Fact]
    public void SetSessionState_WithoutTelemetry_ClosesThePicker()
    {
        using var picker = CreateLoadedPicker();
        picker.OpenCommand.Execute(null);

        picker.SetSessionState(SessionTimestamp, durationSeconds: null, range: null);

        Assert.False(picker.IsOpen);
        Assert.False(picker.CanOpen);
    }

    private SegmentPickerViewModel CreateLoadedPicker(double durationSeconds = 10, TelemetryTimeRange? analysisRange = null)
    {
        var picker = new SegmentPickerViewModel(
            sessionId,
            segmentStore,
            segmentCoordinator,
            dialogService,
            analysisRanges.Add,
            errors.Add);
        picker.SetSessionState(SessionTimestamp, durationSeconds, analysisRange);
        return picker;
    }

    private SessionSegmentSnapshot AddSegment(string name, double startSeconds, double endSeconds, Guid? segmentSessionId = null)
    {
        var segment = new SessionSegmentSnapshot(Guid.NewGuid(), segmentSessionId ?? sessionId, name, startSeconds, endSeconds);
        segments.AddOrUpdate(segment);
        return segment;
    }

    private void AnswerNamePrompt(Action<SegmentNamePromptViewModel> answer)
    {
        dialogService.ShowContentDialogAsync(Arg.Any<SegmentNamePromptViewModel>(), Arg.Any<DialogOptions>())
            .Returns(call =>
            {
                answer(call.ArgAt<SegmentNamePromptViewModel>(0));
                return PromptResult.Ok;
            });
    }
}
