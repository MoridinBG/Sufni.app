using System.Reactive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Shared.Formatting;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.ViewModels;

/// <summary>One selectable entry of the scope: a preset or a legend class with its distance inside the time range.</summary>
public sealed class TerrainScopeOption : ObservableObject
{
    private readonly Func<TerrainScope, bool> isSelected;
    private readonly Func<TerrainScope, TerrainProfile, bool> isEnabled;
    private readonly Func<TerrainSelectionResult, double> metres;

    internal TerrainScopeOption(string label, Enum? terrainClass, Action select, Func<TerrainScope, bool> isSelected,
        Func<TerrainScope, TerrainProfile, bool> isEnabled, Func<TerrainSelectionResult, double> metres)
    {
        Label = label;
        Class = terrainClass;
        Select = new RelayCommand(select);
        this.isSelected = isSelected;
        this.isEnabled = isEnabled;
        this.metres = metres;
    }

    public string Label { get; }

    /// <summary>The grade band or roughness level of a legend entry; null for a preset.</summary>
    public Enum? Class { get; }
    public IRelayCommand Select { get; }
    public bool IsChecked { get; private set => SetProperty(ref field, value); }
    public bool IsEnabled { get; private set => SetProperty(ref field, value); }
    public double DistanceMetres
    {
        get;
        private set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(DistanceText));
        }
    }

    public string DistanceText => UnitsFormatter.FormatDistance(DistanceMetres);

    internal void Update(TerrainScope? scope, TerrainProfile? profile, TerrainSelectionResult? result)
    {
        IsChecked = scope is not null && isSelected(scope);
        IsEnabled = scope is not null && profile is not null && isEnabled(scope, profile);
        DistanceMetres = result is null ? 0 : metres(result);
    }
}

public sealed class TerrainScopeViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable subscription;
    public IReadOnlyList<TerrainScopeOption> Presets { get; }
    public IReadOnlyList<TerrainScopeOption> Roughness { get; }
    public IReadOnlyList<TerrainScopeOption> Grade { get; }

    /// <summary>A terrain profile is loaded and no segment comparison replaces the scope.</summary>
    public bool Available { get; private set => SetProperty(ref field, value); }
    public bool IsDefault { get; private set => SetProperty(ref field, value); }
    public bool IsEmpty { get; private set => SetProperty(ref field, value); }
    public double SelectedMetres { get; private set => SetProperty(ref field, value); }

    /// <summary>The selected ground of a chosen scope, for plot overlays. The default scope stays unmarked.</summary>
    public TelemetryTimeSelection? Selection { get; private set => SetProperty(ref field, value); }
    public string Label { get; private set => SetProperty(ref field, value); } = string.Empty;
    public string ChipText { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>A chosen or empty scope changes what the pages show, so its banner stands out.</summary>
    public bool Highlighted { get; private set => SetProperty(ref field, value); }
    public string BannerText { get; private set => SetProperty(ref field, value); } = string.Empty;
    public string? Note { get; private set => SetProperty(ref field, value); }
    public IRelayCommand Reset { get; }
    public IRelayCommand UseAll { get; }

    internal TerrainScopeViewModel(IObservable<RecordedSessionEditorState> states, RecordedSessionEditorActions actions)
    {
        Presets = Enum.GetValues<TerrainPreset>().Select(preset => new TerrainScopeOption(TerrainScopeText.Name(preset), null,
            () => actions.SelectTerrainPreset(preset), s => s.Preset == preset,
            (_, p) => preset == TerrainPreset.All || p.GradeState == TerrainGradeState.Available,
            r => r.PresetMetres(preset))).ToArray();
        Roughness = Enum.GetValues<RoughnessClass>().Select(value => new TerrainScopeOption(TerrainClassText.Name(value), value,
            () => actions.ToggleRoughness(value), s => s.Filter.Contains(value), (s, _) => s.Admits(value),
            r => r.RoughnessMetres[(int)value])).ToArray();
        Grade = Enum.GetValues<GradeBand>().Select(value => new TerrainScopeOption(TerrainClassText.Name(value), value,
            () => actions.ToggleGrade(value), s => s.Filter.Contains(value), (s, _) => s.Admits(value),
            r => r.GradeMetres[(int)value])).ToArray();
        Reset = new RelayCommand(actions.ResetTerrainScope);
        UseAll = new RelayCommand(() => actions.SelectTerrainPreset(TerrainPreset.All));
        subscription = states.Select(state => (Profile: (state.Terrain as RecordedTerrainState.Loaded)?.Profile,
                Choice: state.Intent.Scope, Scope: state.TerrainScope, Result: state.TerrainSelection))
            .DistinctUntilChanged().Subscribe(input =>
        {
            var (profile, choice, scope, result) = input;
            var applied = choice is AnalysisScope.SegmentComparison ? null : scope;
            Available = applied is not null && result is not null;
            IsDefault = choice is AnalysisScope.Default;
            IsEmpty = Available && result!.Selection.Ranges.IsEmpty;
            SelectedMetres = Available ? result!.SelectedMetres : 0;
            Selection = Available && !IsDefault ? result!.Selection : null;
            Label = applied is null ? string.Empty : TerrainScopeText.Label(applied);
            ChipText = Available ? TerrainScopeText.Chip(applied!, result!) : string.Empty;
            Highlighted = Available && (!IsDefault || IsEmpty);
            BannerText = Available ? IsEmpty ? TerrainScopeText.Empty(applied!) : TerrainScopeText.Banner(applied!, result!) : string.Empty;
            Note = profile is null ? null : TerrainScopeText.NoGrade(profile.GradeState);
            foreach (var option in Presets.Concat(Roughness).Concat(Grade)) option.Update(applied, profile, result);
        });
    }

    public void Dispose() => subscription.Dispose();
}
