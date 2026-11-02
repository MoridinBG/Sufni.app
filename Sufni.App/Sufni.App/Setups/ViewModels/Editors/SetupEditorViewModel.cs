using Sufni.App.Setups.Services;
using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using Sufni.Kinematics;
using BikeModel = Sufni.App.Bikes.Models.Bike;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageParts;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.SensorConfigurations;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Common;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Bikes.ViewModels.Editors.BikeEditorParts;
namespace Sufni.App.Setups.ViewModels.Editors;

/// <summary>
/// Setup editor with one immutable draft base. Sensor sub-editors expose draft
/// inputs; the coordinator owns validation, serialization and persistence.
/// </summary>
public partial class SetupEditorViewModel : TabPageViewModelBase
{
    public Guid Id { get; private set; }
    public bool IsInDatabase { get; private set; }

    #region Private fields

    private readonly ISetupCoordinator setupCoordinator;
    private readonly IBikeStore bikeStore;
    private readonly ObservableCollectionExtended<BikeSnapshot> bikesSource = new();
    private readonly CancellableOperation importOperation = new();
    private SetupDraft draftBase;

    #endregion Private fields

    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial Guid? BoardId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial BikeSnapshot? SelectedBike { get; set; }

    [ObservableProperty] public partial SensorType? ForkSensorType { get; set; }
    [ObservableProperty] public partial SensorType? ShockSensorType { get; set; }
    [ObservableProperty] public partial IReadOnlyList<SensorType?> ShockSensorTypes { get; set; } = [null];
    [ObservableProperty] public partial string RearSuspensionDescription { get; set; } = "Hardtail";
    [ObservableProperty] public partial string? RearSensorCompatibilityMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial SensorConfigurationViewModel? ForkSensorConfiguration { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial SensorConfigurationViewModel? ShockSensorConfiguration { get; set; }

    public ReadOnlyObservableCollection<BikeSnapshot> Bikes { get; }
    public List<SensorType?> ForkSensorTypes { get; } = [null, .. Enum.GetValues<SensorType>().Where(t => t.ToString().EndsWith("Fork"))];

    #endregion

    #region Property change handlers

    // RotationalShockSensorConfigurationViewModel needs a list of Joints, so that sensor position
    // can be defined.
    partial void OnSelectedBikeChanged(BikeSnapshot? value)
    {
        UpdateShockSensorCapabilities(value);

        if (ShockSensorConfiguration is RotationalShockSensorConfigurationViewModel sensorConfiguration)
        {
            sensorConfiguration.JointViewModels = JointsFromSnapshot(value);
        }
    }

    partial void OnForkSensorTypeChanged(SensorType? value)
    {
        if (ForkSensorConfiguration is not null && value == ForkSensorConfiguration.Type) return;
        ForkSensorConfiguration = SensorConfigurationViewModel.Create(value);
    }

    partial void OnShockSensorTypeChanged(SensorType? value)
    {
        RearSensorCompatibilityMessage = null;
        if (ShockSensorConfiguration is not null && value == ShockSensorConfiguration.Type) return;
        ShockSensorConfiguration = SensorConfigurationViewModel.Create(value, JointsFromSnapshot(SelectedBike));
    }

    partial void OnForkSensorConfigurationChanged(SensorConfigurationViewModel? oldValue, SensorConfigurationViewModel? newValue)
    {
        UpdateSensorConfigurationSubscription(oldValue, newValue);
    }

    partial void OnShockSensorConfigurationChanged(SensorConfigurationViewModel? oldValue, SensorConfigurationViewModel? newValue)
    {
        UpdateSensorConfigurationSubscription(oldValue, newValue);
    }

    private void UpdateSensorConfigurationSubscription(SensorConfigurationViewModel? oldValue, SensorConfigurationViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSensorConfigurationPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSensorConfigurationPropertyChanged;
        }
    }

    #endregion Property change handlers

    #region Constructors

    public SetupEditorViewModel(
        SetupSnapshot snapshot,
        bool isNew,
        IBikeStore bikeStore,
        ISetupCoordinator setupCoordinator,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider,
        Guid? boardId = null)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.setupCoordinator = setupCoordinator;
        this.bikeStore = bikeStore;
        Bikes = new ReadOnlyObservableCollection<BikeSnapshot>(bikesSource);

        IsInDatabase = !isNew;
        Id = snapshot.Id;
        draftBase = SetupDraft.From(snapshot, boardId);

        ResetImplementation();
        EvaluateDirtiness();
    }

    #endregion

    #region Private methods

    private static ObservableCollection<JointViewModel> JointsFromSnapshot(BikeSnapshot? snapshot)
    {
        if (snapshot?.RearSuspension is not RearSuspensionSpec.Linkage linkage) return [];

        return [.. linkage.Spec.Joints.Select(joint =>
            new JointViewModel(joint.Name, joint.Type, joint.X, joint.Y))];
    }

    private static IReadOnlyList<SensorType?> AllowedShockSensorTypes(BikeSnapshot? bike) => bike?.RearSuspension switch
    {
        RearSuspensionSpec.Linkage => [null, SensorType.LinearShock, SensorType.RotationalShock],
        RearSuspensionSpec.LeverageRatio => [null, SensorType.LinearShockStroke],
        _ => [null],
    };

    private static string RearSuspensionDescriptionFor(BikeSnapshot? bike) => bike?.RearSuspension switch
    {
        RearSuspensionSpec.Linkage => "Linkage",
        RearSuspensionSpec.LeverageRatio => "Leverage ratio",
        _ => "Hardtail",
    };

    private void UpdateShockSensorCapabilities(BikeSnapshot? bike)
    {
        RearSuspensionDescription = RearSuspensionDescriptionFor(bike);
        ShockSensorTypes = AllowedShockSensorTypes(bike);

        if (ShockSensorConfiguration is not null &&
            ShockSensorTypes.Contains(ShockSensorConfiguration.Type))
        {
            if (ShockSensorType != ShockSensorConfiguration.Type)
            {
                ShockSensorType = ShockSensorConfiguration.Type;
            }
            else
            {
                OnPropertyChanged(nameof(ShockSensorType));
            }

            return;
        }

        if (ShockSensorType is null || ShockSensorTypes.Contains(ShockSensorType))
        {
            return;
        }

        ShockSensorType = null;
        ShockSensorConfiguration = null;
        RearSensorCompatibilityMessage = "Rear sensor cleared because the selected bike does not support this sensor.";
    }

    private void OnSensorConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
    }

    #endregion Private methods

    #region TabPageViewModelBase overrides

    private SetupDraft CaptureDraft() => new(Id, Name, SelectedBike?.Id ?? Guid.Empty, BoardId,
        ForkSensorConfiguration?.CaptureDraft(), ShockSensorConfiguration?.CaptureDraft());

    protected override void EvaluateDirtiness() => IsDirty = !IsInDatabase || CaptureDraft() != draftBase;

    protected override bool CanSave()
    {
        EvaluateDirtiness();
        return IsDirty && SetupSaveRules.Validate(CaptureDraft(), SelectedBike) is null;
    }

    protected override async Task SaveImplementation()
    {
        var submitted = CaptureDraft();
        var result = await setupCoordinator.SaveAsync(submitted, IsInDatabase ? draftBase : null);

        switch (result)
        {
            case SetupSaveResult.Saved saved:
                draftBase = SetupDraft.From(saved.Snapshot, saved.BoardId);
                IsInDatabase = true;
                EvaluateDirtiness();
                NotifyEditorCommandStateChanged();
                if (!IsDirty) shell.CompleteEdit(new EditorKey.Setup(saved.Snapshot.Id));
                break;

            case SetupSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Setup changed elsewhere",
                    "This setup has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    draftBase = SetupDraft.From(conflict.CurrentSnapshot, conflict.BoardId);
                    await ResetImplementation();
                    EvaluateDirtiness();
                }
                break;

            case SetupSaveResult.Missing:
                Notifications.Add("The setup is no longer available.");
                break;
            case SetupSaveResult.Invalid invalid:
                ErrorMessages.Add(FailurePresentation.From(invalid.Reason));
                break;
            case SetupSaveResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    protected override Task ResetImplementation()
    {
        Name = draftBase.Name;
        BoardId = draftBase.BoardId;
        SelectedBike = bikeStore.Get(draftBase.BikeId);
        ForkSensorConfiguration = SensorConfigurationViewModel.FromDraft(draftBase.FrontSensor);
        ShockSensorConfiguration = SensorConfigurationViewModel.FromDraft(draftBase.RearSensor, JointsFromSnapshot(SelectedBike));
        ForkSensorType = ForkSensorConfiguration?.Type;
        ShockSensorType = ShockSensorConfiguration?.Type;

        return Task.CompletedTask;
    }

    protected override async Task ExportImplementation()
    {
        var result = await setupCoordinator.ExportSetupAsync(draftBase);
        if (result is SetupExportResult.Failed failed)
        {
            ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
        }
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private System.Threading.Tasks.Task EditBike(BikeSnapshot? bike)
    {
        if (bike is null) return System.Threading.Tasks.Task.CompletedTask;
        shell.OpenEditor(new EditorKey.Bike(bike.Id));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    protected override async Task DeleteImplementation(bool navigateBack)
    {
        if (!IsInDatabase)
        {
            await shell.CloseEditorAsync(new EditorKey.Setup(Id));
            return;
        }

        var result = await setupCoordinator.DeleteAsync(Id);
        switch (result)
        {
            case SetupDeleteResult.Deleted:
                await shell.CloseEditorAsync(new EditorKey.Setup(Id));
                break;
            case SetupDeleteResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (IsDirty)
        {
            var discard = await dialogService.ShowConfirmationAsync(
                "Discard unsaved changes?",
                "Importing will replace your unsaved changes. Continue?");
            if (!discard) return;
        }

        var token = importOperation.Start();
        try
        {
            var result = await setupCoordinator.ImportSetupAsync(token);
            if (token.IsCancellationRequested) return;

            switch (result)
            {
                case SetupImportResult.Imported imported:
                    await ApplyImportedSetupAsync(imported.Data);
                    break;
                case SetupImportResult.InvalidFile invalid:
                    ErrorMessages.Add(new FailurePresentation(invalid.ErrorMessage));
                    break;
                case SetupImportResult.Failed failed:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task ApplyImportedSetupAsync(ImportedSetupEditorData data)
    {
        Id = data.Setup.Id;
        IsInDatabase = true;
        draftBase = SetupDraft.From(data.Setup, data.BoardId);

        await ResetImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();

        if (data.BoardIdWarning is not null)
        {
            ErrorMessages.Add(new FailurePresentation(data.BoardIdWarning));
        }
    }

    [RelayCommand]
    private void Loaded()
    {
        EnsureScopedSubscription(s => s.Add(
            bikeStore.Connect()
                .Bind(bikesSource)
                .Subscribe()));

        // Re-resolve SelectedBike now the bikesSource is populated.
        SelectedBike = Bikes.FirstOrDefault(b => b.Id == draftBase.BikeId);
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        importOperation.Cancel();

        // Clear the source so a subsequent Loaded rebuilds from scratch.
        bikesSource.Clear();
    }

    #endregion Commands
}
