using Sufni.App.Shared.Models;
using System.Reactive.Subjects;
using DynamicData;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Stores;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Stores;
using Sufni.App.Terrain.ViewModels;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentPickerViewModelTests
{
    private const long SessionTimestamp = 1_767_261_600;

    private readonly Guid sessionId = Guid.NewGuid();
    private readonly SourceCache<SessionSegmentSnapshot, Guid> segments = new(segment => segment.Id);
    private readonly ISessionSegmentStore segmentStore = Substitute.For<ISessionSegmentStore>();
    private readonly ISegmentCoordinator segmentCoordinator = Substitute.For<ISegmentCoordinator>();
    private readonly SourceCache<TerrainLabelSnapshot, Guid> labels = new(label => label.Id);
    private readonly ITerrainLabelStore labelStore = Substitute.For<ITerrainLabelStore>();
    private readonly ITerrainLabelCoordinator labelCoordinator = Substitute.For<ITerrainLabelCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly List<TelemetryTimeRange?> analysisRanges = [];
    private readonly List<FailurePresentation> errors = [];
    private readonly List<string> notifications = [];
    private readonly Subject<SegmentPickerSessionState> sessionState = new();

    public SegmentPickerViewModelTests()
    {
        segmentStore.Connect().Returns(segments.Connect());
        labelStore.Connect().Returns(labels.Connect());
        segmentCoordinator.ValidateName(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>()).Returns((string?)null);
        segmentCoordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new StravaSegmentFetchResult.NotConnected());
    }

    [Fact]
    public void SessionDisappearsAndReturns_SavedRowsRemainAndBecomeVisibleAgain()
    {
        var segment = AddSegment("Source segment", 2, 4);
        using var picker = CreateLoadedPicker();
        Assert.Single(picker.SavedSegments);

        sessionState.OnNext(new SegmentPickerSessionState(null, null, null));
        Assert.Empty(picker.SavedSegments);
        Assert.Equal(segment, segments.Lookup(segment.Id).Value);
        sessionState.OnNext(new SegmentPickerSessionState(SessionTimestamp, 10, null, "source-hash"));

        Assert.Equal(segment.Id, Assert.Single(picker.SavedSegments).Id);
    }

    [Fact]
    public void SavedSegments_ProjectSourceAnchorsIntoRemoteSplitWindow_AndHideUntilIdentityArrives()
    {
        var kept = AddSegment("Climb", 12.25, 14.25);
        AddSegment("Outside window", 8, 11);
        using var picker = CreateLoadedPicker();

        sessionState.OnNext(new SegmentPickerSessionState(SessionTimestamp, 5, null, WindowStartSeconds: 10.25));
        Assert.Empty(picker.SavedSegments);
        sessionState.OnNext(new SegmentPickerSessionState(SessionTimestamp, 5, new TelemetryTimeRange(2, 4), "source-hash", 10.25));

        var row = Assert.Single(picker.SavedSegments);
        Assert.Equal(kept.Id, row.Id);
        Assert.True(row.IsSelected);
        sessionState.OnNext(new SegmentPickerSessionState(SessionTimestamp, 5, null, "source-hash", 10.25));
        picker.SelectSavedSegmentCommand.Execute(Assert.Single(picker.SavedSegments));
        Assert.Equal(new TelemetryTimeRange(2, 4), Assert.Single(analysisRanges));
        Assert.Equal(kept, segments.Lookup(kept.Id).Value);
    }

    [Fact]
    public void SavedSegments_ListsOnlyTheSessionSegmentsInsideTheTelemetry()
    {
        var inside = AddSegment("Inside", 2, 4);
        AddSegment("Trimmed away", 8, 12);
        AddSegment("Other source", 2, 4, "other-source");
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
    public async Task SaveSelectionAsync_WhenSaveFails_ReportsError()
    {
        segmentCoordinator.NextName(sessionId).Returns("Segment 1");
        segmentCoordinator.CreateAsync(sessionId, "Segment 1", 2, 4)
            .Returns(new SegmentChangeResult.Failed(SegmentWriteFailureReason.Create, "technical diagnostic"));
        AnswerNamePrompt(prompt => prompt.ConfirmCommand.Execute(null));
        using var picker = CreateLoadedPicker();

        await picker.SaveSelectionAsync(new TelemetryTimeRange(2, 4));

        var failure = Assert.Single(errors);
        Assert.Equal("technical diagnostic", failure.Details);
        Assert.DoesNotContain(failure.Details, failure.Summary);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task SaveSelectionAsync_WhenSessionIsGone_ReportsNotification()
    {
        segmentCoordinator.NextName(sessionId).Returns("Segment 1");
        segmentCoordinator.CreateAsync(sessionId, "Segment 1", 2, 4)
            .Returns(new SegmentChangeResult.SessionMissing());
        AnswerNamePrompt(prompt => prompt.ConfirmCommand.Execute(null));
        using var picker = CreateLoadedPicker();

        await picker.SaveSelectionAsync(new TelemetryTimeRange(2, 4));

        Assert.Empty(errors);
        Assert.Single(notifications);
    }

    [Fact]
    public async Task DeleteSavedSegment_WhenAlreadyGone_ReportsNotification()
    {
        var segment = AddSegment("Segment", 2, 4);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        segmentCoordinator.DeleteAsync(segment.Id).Returns(new SegmentChangeResult.SegmentMissing());
        using var picker = CreateLoadedPicker();

        await picker.DeleteSavedSegmentCommand.ExecuteAsync(picker.SavedSegments[0]);

        Assert.Empty(errors);
        Assert.Single(notifications);
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
    public void SessionState_WithoutTelemetry_ClosesThePicker()
    {
        using var picker = CreateLoadedPicker();
        picker.OpenCommand.Execute(null);

        sessionState.OnNext(new SegmentPickerSessionState(SessionTimestamp, TelemetryDurationSeconds: null, AnalysisRange: null));

        Assert.False(picker.IsOpen);
        Assert.False(picker.CanOpen);
    }

    [Fact]
    public async Task Open_WhenStravaFails_KeepsDiagnosticsSeparateAndClearsThemOnRetry()
    {
        const string details = "technical diagnostic";
        segmentCoordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new StravaSegmentFetchResult.Failed(StravaSegmentFetchFailureReason.Load, details),
                new StravaSegmentFetchResult.NoMatchingActivity());
        using var picker = CreateLoadedPicker();

        await picker.OpenCommand.ExecuteAsync(null);

        Assert.NotNull(picker.StravaError);
        Assert.Equal(details, picker.StravaError.Details);
        Assert.DoesNotContain(details, picker.StravaError.Summary);
        Assert.Null(picker.StravaMessage);
        picker.CloseCommand.Execute(null);

        await picker.OpenCommand.ExecuteAsync(null);

        Assert.Null(picker.StravaError);
        Assert.True(picker.HasStravaMessage);
    }

    [Fact]
    public async Task ClosedPicker_IgnoresLateSuccessfulStravaResponse()
    {
        var response = new TaskCompletionSource<StravaSegmentFetchResult>();
        segmentCoordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(response.Task);
        using var picker = CreateLoadedPicker();
        var opening = picker.OpenCommand.ExecuteAsync(null);
        picker.CloseCommand.Execute(null);
        response.SetResult(new StravaSegmentFetchResult.Loaded([new("Late effort", 100, new(1, 2))]));

        await opening.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.False(picker.IsOpen);
        Assert.False(picker.IsStravaSegmentsLoading);
        Assert.Empty(picker.StravaSegments);
    }

    [Fact]
    public async Task ClosingConnectDialog_CancelsConnectionBeforeFetchingSegments()
    {
        segmentCoordinator.GetStravaConnectionAsync()
            .Returns(new StravaConnectionSnapshot("client", "secret", false));
        var connection = new TaskCompletionSource<StravaConnectResult>();
        CancellationToken connectionToken = default;
        Task? connecting = null;
        segmentCoordinator.ConnectStravaAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => { connectionToken = call.Arg<CancellationToken>(); return connection.Task; });
        dialogService.ShowContentDialogAsync(Arg.Any<StravaConnectDialogViewModel>(), Arg.Any<DialogOptions>())
            .Returns(call =>
            {
                var dialog = call.ArgAt<StravaConnectDialogViewModel>(0);
                dialog.RedirectUrl = "http://localhost/exchange_token?code=abc";
                dialog.MarkAuthorizationUrlOpened();
                connecting = dialog.ConnectCommand.ExecuteAsync(null);
                return PromptResult.Cancel;
            });
        segmentCoordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.True(connectionToken.IsCancellationRequested);
                return new StravaSegmentFetchResult.NotConnected();
            });
        using var picker = CreateLoadedPicker();

        await picker.ConnectWithStravaCommand.ExecuteAsync(null);
        connection.SetCanceled(connectionToken);
        await connecting!.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.False(picker.IsStravaSegmentsLoading);
    }

    [Fact]
    public void TerrainLabels_HoldTheLabelsInsideTheSessionWindowInTelemetrySeconds()
    {
        var kept = AddLabel(RoughnessClass.Rough, 12.5, 14.5);
        AddLabel(RoughnessClass.Calm, 8, 11);
        AddLabel(RoughnessClass.Calm, 12, 13, ownerKey: "other-source");
        using var picker = CreateLoadedPicker();

        sessionState.OnNext(new SegmentPickerSessionState(SessionTimestamp, 5, null, "source-hash", 10.5));

        Assert.Equal(new TerrainLabelRow(kept.Id, RoughnessClass.Rough, new TelemetryTimeRange(2, 4), false),
            Assert.Single(picker.TerrainLabels));
    }

    [Fact]
    public void SelectTerrainLabel_SetsItsRangeAndClosesThePicker()
    {
        AddLabel(RoughnessClass.Rough, 2, 4);
        using var picker = CreateLoadedPicker();
        picker.OpenCommand.Execute(null);

        picker.SelectTerrainLabelCommand.Execute(picker.TerrainLabels[0]);

        Assert.Equal(new TelemetryTimeRange(2, 4), Assert.Single(analysisRanges));
        Assert.False(picker.IsOpen);
    }

    [Fact]
    public async Task ChangeTerrainLabelClass_CallsTheCoordinatorWithTheChosenClass()
    {
        var label = AddLabel(RoughnessClass.Rough, 2, 4);
        labelCoordinator.SetClassAsync(label.Id, RoughnessClass.Calm).Returns(new TerrainLabelChangeResult.Applied());
        RoughnessClass? offered = null;
        dialogService.AnswerClassPrompt(prompt =>
        {
            offered = prompt.SelectedClass;
            prompt.SelectedClass = RoughnessClass.Calm;
            prompt.ConfirmCommand.Execute(null);
        });
        using var picker = CreateLoadedPicker();

        await picker.ChangeTerrainLabelClassCommand.ExecuteAsync(picker.TerrainLabels[0]);

        Assert.Equal(RoughnessClass.Rough, offered);
        await labelCoordinator.Received(1).SetClassAsync(label.Id, RoughnessClass.Calm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteTerrainLabel_DeletesOnlyAfterConfirmation(bool confirmed)
    {
        var label = AddLabel(RoughnessClass.Rough, 2, 4);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(confirmed);
        labelCoordinator.DeleteAsync(label.Id).Returns(new TerrainLabelChangeResult.Applied());
        using var picker = CreateLoadedPicker();

        await picker.DeleteTerrainLabelCommand.ExecuteAsync(picker.TerrainLabels[0]);

        await labelCoordinator.Received(confirmed ? 1 : 0).DeleteAsync(label.Id);
    }

    [Fact]
    public async Task LabelSelectionAsync_CreatesALabelWithTheClassOfThePrompt()
    {
        labelCoordinator.CreateAsync(sessionId, RoughnessClass.VeryRough, 2, 4).Returns(new TerrainLabelChangeResult.Applied());
        dialogService.AnswerClassPrompt(prompt =>
        {
            prompt.SelectedClass = RoughnessClass.VeryRough;
            prompt.ConfirmCommand.Execute(null);
        });
        using var picker = CreateLoadedPicker();

        await picker.LabelSelectionAsync(new TelemetryTimeRange(2, 4));

        await labelCoordinator.Received(1).CreateAsync(sessionId, RoughnessClass.VeryRough, 2, 4);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task LabelSelectionAsync_ShowsTheOverlapMessageAndCreatesNothing()
    {
        labelCoordinator.ValidateRange(sessionId, 2, 4).Returns("The range overlaps a terrain label.");
        string? shown = null;
        dialogService.AnswerClassPrompt(prompt =>
        {
            shown = prompt.ValidationMessage;
            prompt.CancelCommand.Execute(null);
        });
        using var picker = CreateLoadedPicker();

        await picker.LabelSelectionAsync(new TelemetryTimeRange(2, 4));

        Assert.Equal("The range overlaps a terrain label.", shown);
        await labelCoordinator.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<RoughnessClass>(), Arg.Any<double>(), Arg.Any<double>());
    }

    [Fact]
    public async Task LabelSelectionAsync_WhenTheWriteFails_ReportsError()
    {
        labelCoordinator.CreateAsync(sessionId, RoughnessClass.Rough, 2, 4)
            .Returns(new TerrainLabelChangeResult.Failed(TerrainLabelWriteFailureReason.Create, "storage unavailable"));
        dialogService.AnswerClassPrompt(prompt =>
        {
            prompt.SelectedClass = RoughnessClass.Rough;
            prompt.ConfirmCommand.Execute(null);
        });
        using var picker = CreateLoadedPicker();

        await picker.LabelSelectionAsync(new TelemetryTimeRange(2, 4));

        Assert.Equal(FailurePresentation.From(TerrainLabelWriteFailureReason.Create, "storage unavailable"), Assert.Single(errors));
    }

    private SegmentPickerViewModel CreateLoadedPicker(double durationSeconds = 10, TelemetryTimeRange? analysisRange = null)
    {
        var picker = new SegmentPickerViewModel(
            sessionId,
            sessionState,
            segmentStore,
            segmentCoordinator,
            labelStore,
            labelCoordinator,
            dialogService,
            analysisRanges.Add,
            errors.Add,
            notifications.Add);
        sessionState.OnNext(new SegmentPickerSessionState(SessionTimestamp, durationSeconds, analysisRange, "source-hash"));
        return picker;
    }

    private SessionSegmentSnapshot AddSegment(string name, double startSeconds, double endSeconds, string ownerKey = "source-hash")
    {
        var segment = new SessionSegmentSnapshot(Guid.NewGuid(), ownerKey, name, startSeconds, endSeconds);
        segments.AddOrUpdate(segment);
        return segment;
    }

    private TerrainLabelSnapshot AddLabel(RoughnessClass roughness, double startSeconds, double endSeconds, string ownerKey = "source-hash")
    {
        var label = new TerrainLabelSnapshot(Guid.NewGuid(), ownerKey, startSeconds, endSeconds, roughness);
        labels.AddOrUpdate(label);
        return label;
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
