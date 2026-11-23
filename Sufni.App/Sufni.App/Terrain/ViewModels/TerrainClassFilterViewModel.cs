using System.Reactive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.ViewModels;

public sealed class TerrainClassChip : ObservableObject
{
    private readonly Func<TerrainClassFilter, bool> isSelected;
    private readonly Func<TerrainSelectionResult, double> classMetres;

    internal TerrainClassChip(string label, Action toggle, Func<TerrainClassFilter, bool> isSelected,
        Func<TerrainSelectionResult, double> classMetres)
    {
        Label = label;
        Toggle = new RelayCommand(toggle);
        this.isSelected = isSelected;
        this.classMetres = classMetres;
    }

    public string Label { get; }
    public IRelayCommand Toggle { get; }
    public bool IsChecked { get; private set => SetProperty(ref field, value); }
    public double DistanceMetres { get; private set => SetProperty(ref field, value); }

    internal void Update(TerrainClassFilter filter, TerrainSelectionResult? result)
    {
        IsChecked = isSelected(filter);
        DistanceMetres = result is null ? 0 : classMetres(result);
    }
}

public sealed class TerrainClassFilterViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable subscription;
    public IReadOnlyList<TerrainClassChip> Roughness { get; }
    public IReadOnlyList<TerrainClassChip> Steepness { get; }
    public bool Available { get; private set => SetProperty(ref field, value); }
    public bool IsActive { get; private set => SetProperty(ref field, value); }
    public double SelectedMetres { get; private set => SetProperty(ref field, value); }
    public TelemetryTimeSelection? Selection { get; private set => SetProperty(ref field, value); }
    public string BannerText { get; private set => SetProperty(ref field, value); } = string.Empty;
    public IRelayCommand Clear { get; }

    internal TerrainClassFilterViewModel(IObservable<RecordedSessionEditorState> states, RecordedSessionEditorActions actions)
    {
        Roughness = Enum.GetValues<RoughnessClass>().Select(value => new TerrainClassChip(value.ToString(),
            () => actions.ToggleRoughness(value), f => f.Contains(value), r => r.RoughnessMetres[(int)value])).ToArray();
        Steepness = Enum.GetValues<SteepnessClass>().Select(value => new TerrainClassChip(value.ToString(),
            () => actions.ToggleSteepness(value), f => f.Contains(value), r => r.SteepnessMetres[(int)value])).ToArray();
        Clear = new RelayCommand(actions.ClearClassFilter);
        subscription = states.Select(state => (Available: state.Terrain is RecordedTerrainState.Loaded,
            Scope: state.Intent.Scope as AnalysisScope.ClassFilter, Result: state.Presentation.TerrainSelection))
            .DistinctUntilChanged().Subscribe(input =>
        {
            var (available, scope, result) = input;
            Available = available;
            IsActive = scope is not null;
            Selection = IsActive ? result?.Selection : null;
            SelectedMetres = IsActive ? result?.SelectedMetres ?? 0 : 0;
            foreach (var chip in Roughness) chip.Update(scope?.Filter ?? default, result);
            foreach (var chip in Steepness) chip.Update(scope?.Filter ?? default, result);
            var selected = Roughness.Concat(Steepness).Where(chip => chip.IsChecked).Select(chip => chip.Label);
            BannerText = IsActive ? $"{string.Join(", ", selected)} · {SelectedMetres:0.#} m" : string.Empty;
        });
    }

    public void Dispose() => subscription.Dispose();
}
