using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.ViewModels;

public sealed record SetupTerrainLabelItem(SetupTerrainLabelRow Row)
{
    public string SessionName => Row.SessionName;
    public string DateText => DateTimeOffset.FromUnixTimeSeconds(Row.Timestamp).LocalDateTime.ToString("d", CultureInfo.CurrentCulture);
    public RoughnessClass Class => Row.Class;
    public string ClassName => TerrainClassText.Name(Row.Class);
    public string RangeText => string.Create(CultureInfo.InvariantCulture, $"{Row.Range.StartSeconds:F1}-{Row.Range.EndSeconds:F1}s");
}

/// <summary>
/// The terrain labels of a saved setup and what sets the scale of each sensor with readings or labels. Its commands act
/// at once; they are not part of the setup draft.
/// </summary>
public sealed partial class SetupTerrainLabelsViewModel : ObservableObject, IDisposable
{
    private readonly Guid setupId;
    private readonly ITerrainLabelQuery query;
    private readonly ITerrainLabelCoordinator coordinator;
    private readonly IDialogService dialogService;
    private readonly Action<Guid> openSession;
    private readonly Action<FailurePresentation> reportError;
    private readonly IDisposable subscription;

    public SetupTerrainLabelsViewModel(Guid setupId, ITerrainLabelQuery query, ITerrainLabelCoordinator coordinator,
        IDialogService dialogService, Action<Guid> openSession, Action<FailurePresentation> reportError)
    {
        this.setupId = setupId;
        this.query = query;
        this.coordinator = coordinator;
        this.dialogService = dialogService;
        this.openSession = openSession;
        this.reportError = reportError;
        Refresh();
        subscription = query.Changes.Subscribe(_ => Refresh());
    }

    [ObservableProperty]
    public partial IReadOnlyList<string> ScaleLines { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLabels))]
    public partial IReadOnlyList<SetupTerrainLabelItem> Labels { get; private set; } = [];

    public bool HasLabels => Labels.Count > 0;

    public void Dispose() => subscription.Dispose();

    [RelayCommand]
    private void OpenRide(SetupTerrainLabelItem item) => openSession(item.Row.SessionId);

    [RelayCommand]
    private async Task ChangeClass(SetupTerrainLabelItem item)
    {
        if (await TerrainLabelClassPromptViewModel.AskAsync(dialogService, "Change terrain label", "Change", item.Class, null) is { } roughness)
            Report(await coordinator.SetClassAsync(item.Row.LabelId, roughness));
    }

    [RelayCommand]
    private async Task Delete(SetupTerrainLabelItem item)
    {
        if (await dialogService.ShowConfirmationAsync("Delete terrain label?",
                $"Delete the {item.ClassName} label of {item.SessionName} at {item.RangeText}?"))
            Report(await coordinator.DeleteAsync(item.Row.LabelId));
    }

    private void Refresh()
    {
        var setup = query.ForSetup(setupId);
        ScaleLines = setup.Scale is { } scale ? TerrainSourceText.ScaleLines(scale, sensor => HasData(scale, sensor)) : [];
        Labels = [.. setup.Labels.Select(row => new SetupTerrainLabelItem(row))];
    }

    private static bool HasData(SetupTerrainScale scale, TerrainSensor? sensor) => sensor switch
    {
        TerrainSensor.FrameImu => HasData(scale.FrameImu, scale.Labels.FrameImu),
        TerrainSensor.ForkImu => HasData(scale.ForkImu, scale.Labels.ForkImu),
        TerrainSensor.RearAxleImu => HasData(scale.RearAxleImu, scale.Labels.RearAxleImu),
        null => HasData(scale.FrameClimb, scale.Labels.SmoothClimb + scale.Labels.TechnicalClimb),
        _ => false
    };

    private static bool HasData(SensorScale sensor, double labelledMetres) =>
        sensor is not SensorScale.NotEnoughData { Rides: 0 } || labelledMetres > 0;

    private void Report(TerrainLabelChangeResult result)
    {
        switch (result)
        {
            case TerrainLabelChangeResult.LabelMissing:
                reportError(new FailurePresentation("Terrain label no longer exists."));
                break;
            case TerrainLabelChangeResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }
}
