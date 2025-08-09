using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.App.ViewModels.SensorConfigurations;

namespace Sufni.App.ViewModels.Items;

public sealed partial class SetupViewModel : ItemViewModelBase
{
    public bool IsInDatabase;

    #region Private fields

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
    private BikeViewModel? selectedBike;

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

    public ReadOnlyObservableCollection<ItemViewModelBase> Bikes => bikes;
    private readonly ReadOnlyObservableCollection<ItemViewModelBase> bikes;
    public List<SensorType?> ForkSensorTypes { get; } = [null, .. Enum.GetValues<SensorType>().Where(t => t.ToString().EndsWith("Fork"))];
    public List<SensorType?> ShockSensorTypes { get; } = [null, .. Enum.GetValues<SensorType>().Where(t => t.ToString().EndsWith("Shock"))];

    #endregion

    #region Property change handlers

    // RotationalShockSensorConfigurationViewModel needs a list of Joints, so that sensor position
    // can be defined.
    partial void OnSelectedBikeChanged(BikeViewModel? value)
    {
        if (value is null) return;
        if (ShockSensorConfiguration is RotationalShockSensorConfigurationViewModel sensorConfiguration)
        {
            sensorConfiguration.JointViewModels = value.JointViewModels;
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
        ShockSensorConfiguration = SensorConfigurationViewModel.Create(value, SelectedBike);
    }

    partial void OnForkSensorConfigurationChanged(SensorConfigurationViewModel? value)
    {
        if  (value is null) return;
        value.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "IsDirty") EvaluateDirtiness();
            SaveCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnShockSensorConfigurationChanged(SensorConfigurationViewModel? value)
    {
        if  (value is null) return;
        value.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "IsDirty") EvaluateDirtiness();
            SaveCommand.NotifyCanExecuteChanged();
        };
    }

    #endregion Property change handlers

    #region Constructors

    public SetupViewModel()
    {
        setup = new Setup();
        Id = setup.Id;
        BoardId = originalBoardId = boardId;
        bikes = new ReadOnlyObservableCollection<ItemViewModelBase>([]);
    }

    public SetupViewModel(Setup setup, Guid? boardId, bool fromDatabase, SourceCache<ItemViewModelBase, Guid> bikesSourceCache)
    {
        this.setup = setup;
        IsInDatabase = fromDatabase;
        Id = setup.Id;
        BoardId = originalBoardId = boardId;
        
        bikesSourceCache.Connect()
            .Bind(out bikes)
            .DisposeMany()
            .Subscribe();

        ResetImplementation();
    }

    #endregion

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
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

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
            Id = await databaseService.PutSetupAsync(newSetup);

            // If this setup was already associated with another board, clear that association.
            // Do not delete the board though, it might be picked up later.
            if (originalBoardId.HasValue && IsInDatabase && originalBoardId != BoardId)
            {
                await databaseService.PutBoardAsync(new Board(originalBoardId.Value, null));
            }

            // If the board ID changed, or this is a new setup, associate it with the board ID.
            if (BoardId.HasValue && (!IsInDatabase || originalBoardId != BoardId))
            {
                await databaseService.PutBoardAsync(new Board(BoardId.Value, Id));
            }

            setup = newSetup;
            originalBoardId = BoardId;

            SaveCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();

            // We notify even if the setup was already in the database, since we need to reevaluate
            // if a setup exists for the import page.
            var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
            Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");
            mainPagesViewModel.SetupsPage.OnAdded(this);
            await mainPagesViewModel.ImportSessionsPage.EvaluateSetupExists();

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
        SelectedBike = Bikes.FirstOrDefault(b => b.Id == setup.BikeId) as BikeViewModel;

        ForkSensorConfiguration = SensorConfigurationViewModel.FromJson(setup.FrontSensorConfigurationJson);
        ShockSensorConfiguration = SensorConfigurationViewModel.FromJson(setup.RearSensorConfigurationJson, SelectedBike);
        ForkSensorType = ForkSensorConfiguration?.Type;
        ShockSensorType = ShockSensorConfiguration?.Type;
        
        return Task.CompletedTask;
    }

    #endregion TabPageViewModelBase overrides
}