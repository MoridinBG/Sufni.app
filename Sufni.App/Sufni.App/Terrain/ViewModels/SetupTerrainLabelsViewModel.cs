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

/// <summary>A label of a setup with the ride it belongs to.</summary>
public sealed record SetupTerrainLabelItem(Guid LabelId, Guid SessionId, string SessionName, long Timestamp, TerrainLabelClass Class,
    TelemetryTimeRange Range, LabelReadingText Reading)
{
    public string DateText => DateTimeOffset.FromUnixTimeSeconds(Timestamp).LocalDateTime.ToString("d", CultureInfo.CurrentCulture);
    public string ClassName => TerrainClassText.Name(Class);
    public string RangeText => string.Create(CultureInfo.InvariantCulture, $"{Range.StartSeconds:F1}-{Range.EndSeconds:F1}s");

    public static SetupTerrainLabelItem From(SetupTerrainLabelRow row) => new(row.LabelId, row.SessionId, row.SessionName,
        row.Timestamp, new TerrainLabelClass.Roughness(row.Class), row.Range, LabelReadingText.Of(row.Class, row.Reading));

    public static SetupTerrainLabelItem From(SetupSteepnessLabelRow row) => new(row.LabelId, row.SessionId, row.SessionName,
        row.Timestamp, new TerrainLabelClass.Steepness(row.Band), row.Range, LabelReadingText.Of(row.Band, row.Reading));
}

/// <summary>
/// The roughness and steepness labels of a saved setup and what sets the scale of each sensor with readings or labels.
/// Its commands act at once; they are not part of the setup draft.
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSteepnessLabels))]
    public partial IReadOnlyList<SetupTerrainLabelItem> SteepnessLabels { get; private set; } = [];

    public bool HasSteepnessLabels => SteepnessLabels.Count > 0;

    public void Dispose() => subscription.Dispose();

    [RelayCommand]
    private void OpenRide(SetupTerrainLabelItem item) => openSession(item.SessionId);

    [RelayCommand]
    private async Task ChangeClass(SetupTerrainLabelItem item)
    {
        if (await TerrainLabelClassPromptViewModel.AskAsync(dialogService, $"Change {TerrainClassText.KindName(item.Class.Kind)} label",
                "Change", item.Class.Kind, item.Class, null) is { } chosen)
            Report(await coordinator.SetClassAsync(item.LabelId, chosen));
    }

    [RelayCommand]
    private async Task Delete(SetupTerrainLabelItem item)
    {
        if (await dialogService.ShowConfirmationAsync($"Delete {TerrainClassText.KindName(item.Class.Kind)} label?",
                $"Delete the {item.ClassName} label of {item.SessionName} at {item.RangeText}?"))
            Report(await coordinator.DeleteAsync(item.LabelId));
    }

    private void Refresh()
    {
        var setup = query.ForSetup(setupId);
        ScaleLines = setup.Scale is { } scale ? TerrainSourceText.ScaleLines(scale, sensor => HasData(scale, sensor)) : [];
        Labels = [.. setup.Labels.Select(SetupTerrainLabelItem.From)];
        SteepnessLabels = [.. setup.SteepnessLabels.Select(SetupTerrainLabelItem.From)];
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
