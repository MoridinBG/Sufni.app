using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels.Base;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed partial class BikeEditorViewModel : TabPageViewModelBase
{
    private readonly BikeEditorStateController controller;
    private readonly IBikeCoordinator bikeCoordinator;
    private readonly IBikeDependencyQuery dependencyQuery;
    private readonly BikeDraftLoadController load;
    private readonly BikeImageOpenController imageOpen;
    private readonly BikeEditorAnalysisDemand analysisDemand;
    private readonly IDisposable subscription;
    private Task? cleanup;

    public Guid Id => controller.Current.Baseline.Id;
    public bool IsInDatabase => controller.Current.IsPersisted;
    public BikeEditorPhase Phase => controller.Current.Phase;
    public bool IsBusy => Phase == BikeEditorPhase.Loading || imageOpen.OpenImageCommand.IsRunning || SaveCommand.IsRunning;
    public BikeFrameWorkspaceViewModel Frame { get; }
    public BikeWheelsWorkspaceViewModel Wheels { get; }
    public RearSuspensionWorkspaceViewModel RearSuspension { get; }
    public LinkageWorkspaceViewModel Linkage { get; }
    public LeverageRatioEditorViewModel LeverageRatioEditor { get; }
    public BikeAnalysisWorkspaceViewModel Analysis { get; }
    public BikeEditorViewModel(
        BikeSnapshot snapshot,
        bool isNew,
        IBikeCoordinator bikeCoordinator,
        IBikeDraftLoader draftLoader,
        IRearSuspensionAnalysisQuery analysisQuery,
        IBikeEditorService bikeEditorService,
        IBikeDependencyQuery dependencyQuery,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher,
        InputCapabilities inputCapabilities,
        IBackgroundTaskRunner backgroundTaskRunner,
        TimeProvider timeProvider,
        BikeDraft? initialDraft = null)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.dependencyQuery = dependencyQuery;
        var baseline = initialDraft ?? BikeDraft.From(snapshot);
        controller = new(baseline, !isNew);
        Name = baseline.Name;
        Frame = new(controller);
        Wheels = new(controller);
        RearSuspension = new(controller, new RearSuspensionModeSwitch(controller, inputCapabilities.HasPointer, dialogService));
        Analysis = new(controller);
        LeverageRatioEditor = new(controller, bikeEditorService, inputCapabilities.HasPointer, ErrorMessages.Add);
        var canvas = new BikeImageCanvasViewModel();
        var handoff = new BikeBitmapHandoff(backgroundTaskRunner, canvas);
        imageOpen = new(controller, bikeEditorService, handoff, ErrorMessages.Add, name => Name = name);
        Linkage = new(controller, Wheels, canvas, imageOpen.OpenImageCommand);
        load = new(controller, draftLoader, handoff, ErrorMessages.Add, Notifications.Add, name => Name = name);
        analysisDemand = new(analysisQuery, controller.Actions, timeProvider, ErrorMessages.Add);
        subscription = controller.States.Subscribe(Apply);
        imageOpen.OpenImageCommand.PropertyChanged += OnCommandChanged;
        SaveCommand.PropertyChanged += OnCommandChanged;
        PropertyChanged += OnEditorChanged;
        _ = load.Load(baseline, snapshot.HasImage && initialDraft is null, true, false);
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Name)) EvaluateDirtiness();
    }

    private void OnCommandChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IAsyncRelayCommand.IsRunning)) OnPropertyChanged(nameof(IsBusy));
    }

    private void Apply(BikeEditorState state)
    {
        if (state.Phase == BikeEditorPhase.Loading || (RearSuspension.Phase == BikeEditorPhase.Ready && state.Inputs.Mode != RearSuspension.RearSuspensionMode)) imageOpen.Cancel();
        Linkage.Canvas.Apply(state);
        Frame.Apply(state);
        Wheels.Apply(state);
        RearSuspension.Apply(state);
        LeverageRatioEditor.Apply(state);
        Analysis.Apply(state);
        Linkage.Apply(state);
        EvaluateDirtiness();
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsInDatabase));
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        NotifyDeleteCommandStateChanged();
        imageOpen.OpenImageCommand.NotifyCanExecuteChanged();
        analysisDemand.Apply(state.Request);
    }

    protected override void EvaluateDirtiness()
    {
        var state = controller.Current;
        IsDirty = !state.IsPersisted || (state.Phase == BikeEditorPhase.Ready &&
            (Name != state.Baseline.Name || state.Geometry.InputsDirty));
    }
    protected override bool CanSave() => Phase == BikeEditorPhase.Ready && IsDirty && controller.Current.Geometry.RequiredInputs;
    protected override bool CanReset() => Phase == BikeEditorPhase.Ready && base.CanReset();
    protected override bool CanExport() => Phase == BikeEditorPhase.Ready && base.CanExport();
    private BikeDraft Capture() => controller.Current.Capture(Name ?? $"bike {Id}");

    protected override async Task SaveImplementation()
    {
        if (Linkage.IsLinkageMode) controller.Actions.Align();
        var draft = Capture();
        var result = await bikeCoordinator.SaveAsync(draft, IsInDatabase ? controller.Current.Baseline : null);
        if (Phase == BikeEditorPhase.Closed) return;
        switch (result)
        {
            case BikeSaveResult.Saved saved:
                if (!controller.Actions.Saved(BikeDraft.From(saved.Snapshot, draft.ImageBytes))) return;

                EvaluateDirtiness();

                NotifyDeleteCommandStateChanged();
                if (!IsDirty) shell.CompleteEdit(new EditorKey.Bike(saved.Snapshot.Id));
                break;

            case BikeSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Bike changed elsewhere",
                    "This bike has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    await load.Load(conflict.CurrentDraft, false, true, true, persisted: true);
                    NotifyDeleteCommandStateChanged();
                }
                break;

            case BikeSaveResult.Missing:
                Notifications.Add("The bike is no longer available.");
                break;

            case BikeSaveResult.Invalid invalidRearSuspension:
                ErrorMessages.Add(new FailurePresentation(invalidRearSuspension.ErrorMessage));
                break;

            case BikeSaveResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    protected override bool CanDelete() =>
        !IsInDatabase || !dependencyQuery.IsBikeInUse(Id);

    protected override async Task DeleteImplementation(bool navigateBack)
    {
        if (!IsInDatabase)
        {
            await shell.CloseEditorAsync(new EditorKey.Bike(Id));
            return;
        }

        var result = await bikeCoordinator.DeleteAsync(Id);
        switch (result)
        {
            case BikeDeleteResult.Deleted:
                await shell.CloseEditorAsync(new EditorKey.Bike(Id));
                break;
            case BikeDeleteResult.InUse:
                ErrorMessages.Add(new FailurePresentation("Bike is referenced by a setup and cannot be deleted."));
                break;
            case BikeDeleteResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    protected override async Task ResetImplementation()
    {
        await load.Load(controller.Current.Baseline, false, false, true);
        await analysisDemand.Pending;
    }

    protected override async Task ExportImplementation()
    {
        if (await bikeCoordinator.ExportBikeAsync(Capture()) is BikeExportResult.Failed failed)
            ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
    }

    [RelayCommand]
    private async Task Loaded()
    {
        EnsureScopedSubscription(s => s.Add(dependencyQuery.Changes.Subscribe(_ => NotifyDeleteCommandStateChanged())));
        controller.Actions.Loaded();
        await load.Pending;
        await analysisDemand.Pending;
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        controller.Actions.Unloaded();
        imageOpen.Cancel();
        LeverageRatioEditor.Cancel();
    }

    protected override Task CloseImplementation() => cleanup ??= CloseEditorAsync();

    private async Task CloseEditorAsync()
    {
        load.Dispose();
        Unloaded();
        await load.Pending;
        if (imageOpen.OpenImageCommand.ExecutionTask is { } imageTask) await imageTask;
        controller.Actions.Closed();
        subscription.Dispose();
        analysisDemand.Dispose();
        imageOpen.OpenImageCommand.PropertyChanged -= OnCommandChanged;
        SaveCommand.PropertyChanged -= OnCommandChanged;
        PropertyChanged -= OnEditorChanged;
        imageOpen.Dispose();
        Linkage.Canvas.Dispose();
        LeverageRatioEditor.Dispose();
        controller.Dispose();
    }
}
