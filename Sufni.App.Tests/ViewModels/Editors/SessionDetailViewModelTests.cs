using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.Tests.ViewModels.Editors;

public class SessionDetailViewModelTests
{
    private readonly ISessionCoordinator sessionCoordinator = Substitute.For<ISessionCoordinator>();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public SessionDetailViewModelTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
    }

    private SessionDetailViewModel CreateEditor(SessionSnapshot snapshot, IObservable<SessionSnapshot>? watch = null)
    {
        sessionStore.Watch(snapshot.Id).Returns(watch ?? Observable.Empty<SessionSnapshot>());
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        return new SessionDetailViewModel(snapshot, sessionCoordinator, sessionStore, tileLayerService, shell, dialogService);
    }

    // ----- Construction -----

    [AvaloniaFact]
    public void Construction_FromSnapshot_PopulatesFields()
    {
        var snapshot = TestSnapshots.Session(
            name: "trail run",
            description: "first lap",
            timestamp: 1700000000,
            hasProcessedData: true,
            updated: 9);

        var editor = CreateEditor(snapshot);

        Assert.Equal(snapshot.Id, editor.Id);
        Assert.Equal("trail run", editor.Name);
        Assert.Equal("first lap", editor.NotesPage.Description);
        Assert.NotNull(editor.Timestamp);
        Assert.True(editor.IsComplete);
        Assert.Equal(9, editor.BaselineUpdated);
    }

    // ----- Dirtiness -----

    [AvaloniaFact]
    public void EditingName_MakesSaveCommandExecutable()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);
        Assert.False(editor.SaveCommand.CanExecute(null));

        editor.Name = "renamed";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void EditingForkSpringRate_MakesSaveCommandExecutable()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);

        editor.NotesPage.ForkSettings.SpringRate = "550 lb/in";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    // ----- Save -----

    [AvaloniaFact]
    public async Task Save_HappyPath_RoutesThroughCoordinator_AndUpdatesBaseline()
    {
        var snapshot = TestSnapshots.Session(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Saved(11));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        await sessionCoordinator.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.Id == snapshot.Id && s.Name == "renamed"),
            5);
        Assert.Equal(11, editor.BaselineUpdated);
        Assert.False(editor.IsDirty);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnMobile_DoesNotNavigateDirectly()
    {
        var snapshot = TestSnapshots.Session(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Saved(11));
        TestApp.SetIsDesktop(false);

        await editor.SaveCommand.ExecuteAsync(null);

        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var snapshot = TestSnapshots.Session(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Session(id: snapshot.Id, name: "remote-updated", updated: 12);
        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("remote-updated", editor.Name);
        Assert.Equal(12, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var snapshot = TestSnapshots.Session(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Session(id: snapshot.Id, name: "remote-updated", updated: 12);
        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("renamed", editor.Name);
        Assert.Equal(5, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnFailed_AppendsErrorMessage()
    {
        var snapshot = TestSnapshots.Session(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Failed("disk full"));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    // ----- Delete -----

    [AvaloniaFact]
    public async Task Delete_HappyPath_NavigatesBack()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);
        sessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult(SessionDeleteOutcome.Deleted));

        await editor.DeleteCommand.ExecuteAsync(true);

        shell.Received(1).GoBack();
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Delete_Failed_AppendsErrorMessage_AndDoesNotNavigateBack()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);
        sessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult(SessionDeleteOutcome.Failed, "locked"));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    // ----- Load / unload -----

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesCoordinatorResult()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = TestTelemetryData.Create();
        var trackPoints = new List<TrackPoint> { new(1, 1, 1, 0) };
        var fullTrackPoints = new List<TrackPoint> { new(2, 2, 2, 0) };
        var result = new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            Guid.NewGuid(),
            fullTrackPoints,
            trackPoints,
            400.0,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8)));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(result);
        TestApp.SetIsDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Same(telemetry, editor.TelemetryData);
        Assert.Same(trackPoints, editor.TrackPoints);
        Assert.Same(fullTrackPoints, editor.FullTrackPoints);
        Assert.Equal(400.0, editor.MapVideoWidth);
        Assert.Equal(1, editor.DamperPage.FrontHscPercentage);
        Assert.True(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_AppliesCacheResult_AndRemovesBalancePageWhenUnavailable()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var result = new SessionMobileLoadResult.LoadedFromCache(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            false));
        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        TestApp.SetIsDesktop(false);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        var springPage = editor.Pages.OfType<SpringPageViewModel>().Single();

        Assert.Equal("front-travel", springPage.FrontTravelHistogram);
        Assert.Equal("front-velocity", editor.DamperPage.FrontVelocityHistogram);
        Assert.True(editor.IsComplete);
        Assert.DoesNotContain(editor.Pages, page => page.DisplayName == "Balance");
    }

    [AvaloniaFact]
    public async Task Loaded_WhenTelemetryPending_LeavesIncompleteWithoutError()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        TestApp.SetIsDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.False(editor.IsComplete);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_WhenCoordinatorFails_AppendsError()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.Failed("boom"));
        TestApp.SetIsDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Unloaded_CancelsInFlightLoad_AndDropsResult()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = TestTelemetryData.Create();
        var pending = new TaskCompletionSource<SessionDesktopLoadResult>();

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.ArgAt<CancellationToken>(1)));
        TestApp.SetIsDesktop(true);

        var editor = CreateEditor(snapshot);
        var loadTask = editor.LoadedCommand.ExecuteAsync(null);
        await Task.Yield();

        editor.UnloadedCommand.Execute(null);
        pending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8))));

        await loadTask;

        Assert.Null(editor.TelemetryData);
        Assert.False(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task Unloaded_OnMobile_CancelsInFlightLoad_AndDropsCacheResult()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var pending = new TaskCompletionSource<SessionMobileLoadResult>();

        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.ArgAt<CancellationToken>(2)));
        TestApp.SetIsDesktop(false);

        var editor = CreateEditor(snapshot);
        var loadTask = editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        await Task.Yield();

        editor.UnloadedCommand.Execute(null);
        pending.SetResult(new SessionMobileLoadResult.BuiltCache(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            false)));

        await loadTask;

        var springPage = editor.Pages.OfType<SpringPageViewModel>().Single();
        Assert.Null(springPage.FrontTravelHistogram);
        Assert.Null(editor.DamperPage.FrontVelocityHistogram);
        Assert.False(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task LaterLoad_SupersedesEarlierCompletion()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var firstTelemetry = TestTelemetryData.Create();
        var secondTelemetry = TestTelemetryData.Create();
        var firstPending = new TaskCompletionSource<SessionDesktopLoadResult>();
        var callCount = 0;

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return callCount == 1
                    ? AwaitWithCancellation(firstPending.Task, callInfo.ArgAt<CancellationToken>(1))
                    : Task.FromResult<SessionDesktopLoadResult>(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
                        secondTelemetry,
                        null,
                        null,
                        null,
                        null,
                        new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80))));
            });
        TestApp.SetIsDesktop(true);

        var editor = CreateEditor(snapshot);
        var firstLoad = editor.LoadedCommand.ExecuteAsync(null);
        await Task.Yield();

        var secondLoad = editor.LoadedCommand.ExecuteAsync(null);
        firstPending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            firstTelemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8))));

        await Task.WhenAll(firstLoad, secondLoad);

        Assert.Same(secondTelemetry, editor.TelemetryData);
        Assert.Equal(10, editor.DamperPage.FrontHscPercentage);
    }

    [AvaloniaFact]
    public async Task ResetImplementation_DoesNotEmitTransientDirtyState_DuringBulkAssignment()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);
        var replacementSession = new Session(snapshot.Id, snapshot.Name, "fresh notes", snapshot.SetupId, snapshot.Timestamp)
        {
            FrontSpringRate = "500 lb/in",
            FrontHighSpeedCompression = 4,
            RearSpringRate = "450 lb/in",
            RearLowSpeedRebound = 7,
            Updated = snapshot.Updated,
        };
        var dirtyTransitions = new List<bool>();

        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionDetailViewModel.IsDirty))
            {
                dirtyTransitions.Add(editor.IsDirty);
            }
        };

        SetCurrentSession(editor, replacementSession);
        await InvokeResetImplementationAsync(editor);

        Assert.DoesNotContain(true, dirtyTransitions);
        Assert.False(editor.IsDirty);
        Assert.Equal("fresh notes", editor.NotesPage.Description);
        Assert.Equal("500 lb/in", editor.NotesPage.ForkSettings.SpringRate);
        Assert.Equal<uint?>(7, editor.NotesPage.ShockSettings.LowSpeedRebound);
    }

    [AvaloniaFact]
    public async Task WatchRefreshes_AreCoalescedWhileLoadIsInFlight()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var watch = new Subject<SessionSnapshot>();
        var initialResult = new SessionDesktopLoadResult.TelemetryPending();
        var refreshLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshPending = new TaskCompletionSource<SessionDesktopLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalPending = new TaskCompletionSource<SessionDesktopLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalTelemetry = TestTelemetryData.Create();
        var finalResultApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cancellationToken = callInfo.ArgAt<CancellationToken>(1);
                callCount++;
                return callCount switch
                {
                    1 => Task.FromResult<SessionDesktopLoadResult>(initialResult),
                    2 => StartRefreshLoad(refreshPending.Task, cancellationToken),
                    _ => StartFinalLoad(finalPending.Task, cancellationToken)
                };

                Task<SessionDesktopLoadResult> StartRefreshLoad(
                    Task<SessionDesktopLoadResult> task,
                    CancellationToken token)
                {
                    refreshLoadStarted.TrySetResult();
                    return AwaitWithCancellation(task, token);
                }

                Task<SessionDesktopLoadResult> StartFinalLoad(
                    Task<SessionDesktopLoadResult> task,
                    CancellationToken token)
                {
                    finalLoadStarted.TrySetResult();
                    return AwaitWithCancellation(task, token);
                }
            });
        TestApp.SetIsDesktop(true);

        var editor = CreateEditor(snapshot, watch.AsObservable());

        void MarkWhenFinalStateApplied()
        {
            if (ReferenceEquals(editor.TelemetryData, finalTelemetry) &&
                editor.DamperPage.FrontHscPercentage == 1)
            {
                finalResultApplied.TrySetResult();
            }
        }

        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SessionDetailViewModel.TelemetryData))
            {
                MarkWhenFinalStateApplied();
            }
        };
        editor.DamperPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DamperPageViewModel.FrontHscPercentage))
            {
                MarkWhenFinalStateApplied();
            }
        };

        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(snapshot with { HasProcessedData = true });
        await refreshLoadStarted.Task;
        watch.OnNext(snapshot with { HasProcessedData = false });
        watch.OnNext(snapshot with { HasProcessedData = true });
        refreshPending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            TestTelemetryData.Create(),
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(9, 9, 9, 9, 9, 9, 9, 9))));

        await finalLoadStarted.Task;
        finalPending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            finalTelemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8))));

        await finalResultApplied.Task;

        Assert.Same(finalTelemetry, editor.TelemetryData);
        await sessionCoordinator.Received(3).LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>());
        watch.Dispose();
    }

    private static void SetCurrentSession(SessionDetailViewModel editor, Session session)
    {
        typeof(SessionDetailViewModel)
            .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(editor, session);
    }

    private static async Task InvokeResetImplementationAsync(SessionDetailViewModel editor)
    {
        var resetMethod = typeof(SessionDetailViewModel)
            .GetMethod("ResetImplementation", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)resetMethod.Invoke(editor, null)!;
    }

    private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
    {
        return await task.WaitAsync(cancellationToken);
    }
}
