using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
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
public partial class SetupEditorViewModel : TabPageViewModelBase
{
    public Guid Id { get; private set; }
    public long BaselineUpdated { get; private set; }
    public bool IsInDatabase { get; private set; }

    #region Private fields

    private readonly IDatabaseService databaseService;
    private readonly IBikeCoordinator bikeCoordinator;
    private readonly ReadOnlyObservableCollection<BikeSnapshot> bikes;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private SensorConfigurationViewModel? forkSensorConfiguration;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private SensorConfigurationViewModel? shockSensorConfiguration;

    public ReadOnlyObservableCollection<BikeSnapshot> Bikes => bikes;
    public List<SensorType?> ForkSensorTypes { get; } = [null, .. Enum.GetValues<SensorType>().Where(t => t.ToString().EndsWith("Fork"))];
    public List<SensorType?> ShockSensorTypes { get; } = [null, .. Enum.GetValues<SensorType>().Where(t => t.ToString().EndsWith("Shock"))];

    #endregion

    #region Property change handlers

    // RotationalShockSensorConfigurationViewModel needs a list of Joints, so that sensor position
    // can be defined.
    partial void OnSelectedBikeChanged(BikeSnapshot? value)
    {
        if (value is null) return;
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
        databaseService = null!;
        bikeCoordinator = null!;
        bikes = new ReadOnlyObservableCollection<BikeSnapshot>([]);
        setup = new Setup();
        Id = setup.Id;
        IsInDatabase = false;
    }

    public SetupEditorViewModel(
        SetupSnapshot snapshot,
        bool isNew,
        IBikeStore bikeStore,
        IBikeCoordinator bikeCoordinator,
        IDatabaseService databaseService,
        INavigator navigator,
        IDialogService dialogService)
        : base(navigator, dialogService)
    {
        this.databaseService = databaseService;
        this.bikeCoordinator = bikeCoordinator;

        bikeStore.Connect()
            .Bind(out bikes)
            .Subscribe();

        IsInDatabase = !isNew;
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
        setup = SetupFromSnapshot(snapshot);
        originalBoardId = snapshot.BoardId;

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
        if (snapshot?.Linkage is null || snapshot.Image is null) return [];

        var jvms = snapshot.Linkage.Joints
            .Select(j => JointViewModel.FromJoint(j, snapshot.Image.Size.Height, snapshot.PixelsToMillimeters));
        return [.. jvms];
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
        try
        {
            ForkSensorConfiguration?.Save();
            ShockSensorConfiguration?.Save();

            var newSetup = new Setup(Id, Name ?? $"setup #{Id}")
            {
                BikeId = SelectedBike?.Id ?? Guid.Empty,
                FrontSensorConfigurationJson = ForkSensorConfiguration?.ToJson(),
                RearSensorConfigurationJson = ShockSensorConfiguration?.ToJson()
            };
            Id = await databaseService.PutAsync(newSetup);

            // If this setup was already associated with another board, clear that association.
            // Do not delete the board though, it might be picked up later.
            if (originalBoardId.HasValue && IsInDatabase && originalBoardId != BoardId)
            {
                await databaseService.PutAsync(new Board(originalBoardId.Value, null));
            }

            // If the board ID changed, or this is a new setup, associate it with the board ID.
            if (BoardId.HasValue && (!IsInDatabase || originalBoardId != BoardId))
            {
                await databaseService.PutAsync(new Board(BoardId.Value, Id));
            }

            setup = newSetup;
            originalBoardId = BoardId;
            BaselineUpdated = newSetup.Updated;

            SaveCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();

            IsInDatabase = true;
            EvaluateDirtiness();

            Debug.Assert(App.Current is not null);
            if (!App.Current.IsDesktop)
            {
                OpenPreviousPage();
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Setup could not be saved: {e.Message}");
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

    #endregion Commands
}
