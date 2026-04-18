using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.App.ViewModels.SensorConfigurations;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Editor view model for a setup. Created by <c>SetupCoordinator</c>
/// from a <see cref="SetupSnapshot"/>; the snapshot's <c>Updated</c>
/// value is kept as <see cref="BaselineUpdated"/> for optimistic
/// conflict detection at save time. The bike combobox is populated
/// from <see cref="IBikeStore"/>.
/// </summary>
public partial class SetupEditorViewModel : TabPageViewModelBase, IEditorActions
{
    public Guid Id { get; private set; }
    public long BaselineUpdated { get; private set; }
    public bool IsInDatabase { get; private set; }

    // Explicit interface implementation: the generated commands are
    // IAsyncRelayCommand[<T>] which C# does not implicitly satisfy a
    // non-generic IRelayCommand interface property with.
    IRelayCommand IEditorActions.OpenPreviousPageCommand => OpenPreviousPageCommand;
    IRelayCommand IEditorActions.SaveCommand => SaveCommand;
    IRelayCommand IEditorActions.ResetCommand => ResetCommand;
    IRelayCommand IEditorActions.DeleteCommand => DeleteCommand;
    IRelayCommand IEditorActions.FakeDeleteCommand => FakeDeleteCommand;

    #region Private fields

    private readonly ISetupCoordinator? setupCoordinator;
    private readonly IBikeCoordinator bikeCoordinator;
    private readonly IBikeStore? bikeStore;
    private readonly ObservableCollectionExtended<BikeSnapshot> bikesSource = new();
    private Setup setup;
    private Guid? originalBoardId;

    #endregion Private fields

    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private Guid? boardId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private BikeSnapshot? selectedBike;

    [ObservableProperty] private SensorType? forkSensorType;
    [ObservableProperty] private SensorType? shockSensorType;
    [ObservableProperty] private IReadOnlyList<SensorType?> shockSensorTypes = [null];
    [ObservableProperty] private string rearSuspensionDescription = "Hardtail";
    [ObservableProperty] private string? rearSensorCompatibilityMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private SensorConfigurationViewModel? forkSensorConfiguration;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private SensorConfigurationViewModel? shockSensorConfiguration;

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

    partial void OnForkSensorConfigurationChanged(SensorConfigurationViewModel? value)
    {
        if (value is null) return;
        value.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "IsDirty") EvaluateDirtiness();
            SaveCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnShockSensorConfigurationChanged(SensorConfigurationViewModel? value)
    {
        if (value is null) return;
        value.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "IsDirty") EvaluateDirtiness();
            SaveCommand.NotifyCanExecuteChanged();
        };
    }

    #endregion Property change handlers

    #region Constructors

    public SetupEditorViewModel()
    {
        setupCoordinator = null;
        bikeCoordinator = null!;
        bikeStore = null;
        Bikes = new ReadOnlyObservableCollection<BikeSnapshot>(bikesSource);
        setup = new Setup();
        Id = setup.Id;
        IsInDatabase = false;
    }

    public SetupEditorViewModel(
        SetupSnapshot snapshot,
        bool isNew,
        IBikeStore bikeStore,
        IBikeCoordinator bikeCoordinator,
        ISetupCoordinator setupCoordinator,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        this.setupCoordinator = setupCoordinator;
        this.bikeCoordinator = bikeCoordinator;
        this.bikeStore = bikeStore;
        Bikes = new ReadOnlyObservableCollection<BikeSnapshot>(bikesSource);

        IsInDatabase = !isNew;
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
        setup = SetupFromSnapshot(snapshot);
        originalBoardId = snapshot.BoardId;

        // Populate name / sensor / board fields immediately so the
        // editor renders correctly even before Loaded fires. SelectedBike
        // is resolved a second time from Loaded, after the bike-store
        // bind subscription publishes its initial snapshot.
        ResetImplementation();
    }

    #endregion

    #region Private methods

    private static Setup SetupFromSnapshot(SetupSnapshot snapshot) => new(snapshot.Id, snapshot.Name)
    {
        BikeId = snapshot.BikeId,
        FrontSensorConfigurationJson = snapshot.FrontSensorConfigurationJson,
        RearSensorConfigurationJson = snapshot.RearSensorConfigurationJson,
        Updated = snapshot.Updated
    };

    private static ObservableCollection<JointViewModel> JointsFromSnapshot(BikeSnapshot? snapshot)
    {
        if (snapshot?.Image is null) return [];

        var resolution = RearSuspensionResolver.Resolve(
            snapshot.RearSuspensionKind,
            snapshot.Linkage,
            snapshot.LeverageRatio);

        if (resolution is not RearSuspensionResolution.Linkage linkage) return [];

        var jvms = linkage.Value.Linkage.Joints
            .Select(j => JointViewModel.FromJoint(j, snapshot.Image.Size.Height, snapshot.PixelsToMillimeters));
        return [.. jvms];
    }

    private static RearSuspensionResolution ResolveRearSuspension(BikeSnapshot? bike) => bike is null
        ? new RearSuspensionResolution.Hardtail()
        : RearSuspensionResolver.Resolve(bike.RearSuspensionKind, bike.Linkage, bike.LeverageRatio);

    private static IReadOnlyList<SensorType?> AllowedShockSensorTypes(RearSuspensionResolution resolution) => resolution switch
    {
        RearSuspensionResolution.Linkage => [null, SensorType.LinearShock, SensorType.RotationalShock],
        RearSuspensionResolution.LeverageRatio => [null, SensorType.LinearShockStroke],
        RearSuspensionResolution.Invalid => [null],
        RearSuspensionResolution.Hardtail => [null],
        _ => [null],
    };

    private static string RearSuspensionDescriptionFor(RearSuspensionResolution resolution) => resolution switch
    {
        RearSuspensionResolution.Linkage => "Linkage",
        RearSuspensionResolution.LeverageRatio => "Leverage ratio",
        RearSuspensionResolution.Invalid => "Invalid rear suspension",
        RearSuspensionResolution.Hardtail => "Hardtail",
        _ => "Hardtail",
    };

    private void UpdateShockSensorCapabilities(BikeSnapshot? bike)
    {
        var resolution = ResolveRearSuspension(bike);
        RearSuspensionDescription = RearSuspensionDescriptionFor(resolution);
        ShockSensorTypes = AllowedShockSensorTypes(resolution);

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

    #endregion Private methods

    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        ForkSensorConfiguration?.EvaluateDirtiness();
        ShockSensorConfiguration?.EvaluateDirtiness();

        IsDirty =
            !IsInDatabase ||
            Name != setup.Name ||
            BoardId != originalBoardId ||
            (SelectedBike is null && setup.BikeId != Guid.Empty) ||
            (SelectedBike is not null && SelectedBike.Id != setup.BikeId) ||
            (ForkSensorConfiguration is null && setup.FrontSensorConfigurationJson is not null) ||
            (ShockSensorConfiguration is null && setup.RearSensorConfigurationJson is not null) ||
            (ForkSensorConfiguration is not null && ForkSensorConfiguration.IsDirty) ||
            (ShockSensorConfiguration is not null && ShockSensorConfiguration.IsDirty);
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();
        return IsDirty &&
               !string.IsNullOrEmpty(Name) &&
               BoardId.HasValue &&
               SelectedBike is not null &&
               (ForkSensorConfiguration is not null || ShockSensorConfiguration is not null) &&
               (ForkSensorConfiguration is null || ForkSensorConfiguration.CanSave()) &&
               (ShockSensorConfiguration is null || ShockSensorConfiguration.CanSave());
    }

    protected override async Task SaveImplementation()
    {
        if (setupCoordinator is null) return;

        ForkSensorConfiguration?.Save();
        ShockSensorConfiguration?.Save();

        var newSetup = new Setup(Id, Name ?? $"setup #{Id}")
        {
            BikeId = SelectedBike?.Id ?? Guid.Empty,
            FrontSensorConfigurationJson = ForkSensorConfiguration?.ToJson(),
            RearSensorConfigurationJson = ShockSensorConfiguration?.ToJson()
        };

        var result = await setupCoordinator.SaveAsync(newSetup, BoardId, BaselineUpdated);

        switch (result)
        {
            case SetupSaveResult.Saved saved:
                setup = newSetup;
                setup.Updated = saved.NewBaselineUpdated;
                originalBoardId = BoardId;
                BaselineUpdated = saved.NewBaselineUpdated;

                SaveCommand.NotifyCanExecuteChanged();
                ResetCommand.NotifyCanExecuteChanged();

                IsInDatabase = true;
                EvaluateDirtiness();
                break;

            case SetupSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Setup changed elsewhere",
                    "This setup has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    setup = SetupFromSnapshot(conflict.CurrentSnapshot);
                    originalBoardId = conflict.CurrentSnapshot.BoardId;
                    BaselineUpdated = conflict.CurrentSnapshot.Updated;
                    await ResetImplementation();
                    EvaluateDirtiness();
                }
                break;

            case SetupSaveResult.Failed failed:
                ErrorMessages.Add($"Setup could not be saved: {failed.ErrorMessage}");
                break;
        }
    }

    protected override Task ResetImplementation()
    {
        Name = setup.Name;
        BoardId = originalBoardId;
        SelectedBike = Bikes.FirstOrDefault(b => b.Id == setup.BikeId);

        ForkSensorConfiguration = SensorConfigurationViewModel.FromJson(setup.FrontSensorConfigurationJson);
        ShockSensorConfiguration = SensorConfigurationViewModel.FromJson(setup.RearSensorConfigurationJson, JointsFromSnapshot(SelectedBike));
        ForkSensorType = ForkSensorConfiguration?.Type;
        ShockSensorType = ShockSensorConfiguration?.Type;

        return Task.CompletedTask;
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private async Task EditBike(BikeSnapshot? bike)
    {
        if (bike is null) return;
        await bikeCoordinator.OpenEditAsync(bike.Id);
    }

    [RelayCommand]
    private async Task Delete(bool navigateBack)
    {
        if (setupCoordinator is null) return;

        if (!IsInDatabase)
        {
            if (navigateBack) OpenPreviousPage();
            return;
        }

        var result = await setupCoordinator.DeleteAsync(Id);
        switch (result.Outcome)
        {
            case SetupDeleteOutcome.Deleted:
                if (navigateBack) OpenPreviousPage();
                break;
            case SetupDeleteOutcome.Failed:
                ErrorMessages.Add($"Setup could not be deleted: {result.ErrorMessage}");
                break;
        }
    }

    [RelayCommand]
    private void FakeDelete()
    {
        // Exists so the editor button strip can bind to a delete command.
    }

    [RelayCommand]
    private void Loaded()
    {
        if (bikeStore is null) return;

        EnsureScopedSubscription(s => s.Add(
            bikeStore.Connect()
                .Bind(bikesSource)
                .Subscribe()));

        // Re-resolve SelectedBike now the bikesSource is populated.
        SelectedBike = Bikes.FirstOrDefault(b => b.Id == setup.BikeId);
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();

        // Clear the source so a subsequent Loaded rebuilds from scratch.
        bikesSource.Clear();
    }

    #endregion Commands
}
