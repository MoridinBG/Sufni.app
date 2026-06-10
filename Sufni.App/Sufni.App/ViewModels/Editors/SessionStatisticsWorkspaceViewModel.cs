using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHost.Views.Controls;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.App.Views.Controls;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

internal sealed class SessionStatisticsWorkspaceViewModel : ObservableObject, ISessionStatisticsWorkspace
{
    private readonly RecordedSessionContext context;
    private readonly Action<TravelHistogramMode> setTravelHistogramMode;
    private readonly Action<BalanceDisplacementMode> setBalanceDisplacementMode;
    private readonly Action<BalanceSpeedMode> setBalanceSpeedMode;
    private readonly Action<VelocityAverageMode> setVelocityAverageMode;
    private readonly Action<SessionAnalysisTargetProfile> setSessionAnalysisTargetProfile;
    private readonly Action<SuspensionType, DampingSpeedCircuit, double> previewDampingSpeedCutoff;
    private readonly Action cancelDampingSpeedCutoffPreview;
    private readonly Func<SuspensionType, DampingSpeedCircuit, double, Task> commitDampingSpeedCutoffAsync;

    public SessionStatisticsWorkspaceViewModel(
        RecordedSessionContext context,
        Action<TravelHistogramMode> setTravelHistogramMode,
        Action<BalanceDisplacementMode> setBalanceDisplacementMode,
        Action<BalanceSpeedMode> setBalanceSpeedMode,
        Action<VelocityAverageMode> setVelocityAverageMode,
        Action<SessionAnalysisTargetProfile> setSessionAnalysisTargetProfile,
        IRelayCommand<TelemetryRangeSelection?> selectTelemetryRangeSelectionCommand,
        Action<SuspensionType, DampingSpeedCircuit, double> previewDampingSpeedCutoff,
        Action cancelDampingSpeedCutoffPreview,
        Func<SuspensionType, DampingSpeedCircuit, double, Task> commitDampingSpeedCutoffAsync)
    {
        this.context = context;
        this.setTravelHistogramMode = setTravelHistogramMode;
        this.setBalanceDisplacementMode = setBalanceDisplacementMode;
        this.setBalanceSpeedMode = setBalanceSpeedMode;
        this.setVelocityAverageMode = setVelocityAverageMode;
        this.setSessionAnalysisTargetProfile = setSessionAnalysisTargetProfile;
        SelectTelemetryRangeSelectionCommand = selectTelemetryRangeSelectionCommand;
        this.previewDampingSpeedCutoff = previewDampingSpeedCutoff;
        this.cancelDampingSpeedCutoffPreview = cancelDampingSpeedCutoffPreview;
        this.commitDampingSpeedCutoffAsync = commitDampingSpeedCutoffAsync;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public TelemetryData? TelemetryData => context.TelemetryData;

    public TelemetryTimeRange? AnalysisRange => context.AnalysisRange;

    public TravelHistogramMode SelectedTravelHistogramMode
    {
        get => context.SelectedTravelHistogramMode;
        set => setTravelHistogramMode(value);
    }

    public BalanceDisplacementMode SelectedBalanceDisplacementMode
    {
        get => context.SelectedBalanceDisplacementMode;
        set => setBalanceDisplacementMode(value);
    }

    public BalanceSpeedMode SelectedBalanceSpeedMode
    {
        get => context.SelectedBalanceSpeedMode;
        set => setBalanceSpeedMode(value);
    }

    public VelocityAverageMode SelectedVelocityAverageMode
    {
        get => context.SelectedVelocityAverageMode;
        set => setVelocityAverageMode(value);
    }

    public SessionAnalysisTargetProfile SelectedSessionAnalysisTargetProfile
    {
        get => context.SelectedSessionAnalysisTargetProfile;
        set => setSessionAnalysisTargetProfile(value);
    }

    public RecordedSessionExtensionSlots ExtensionSlots => context.ExtensionSlots;

    public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } =
        SessionAnalysisPresentation.TravelHistogramModeOptions;

    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
        SessionAnalysisPresentation.BalanceDisplacementModeOptions;

    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
        SessionAnalysisPresentation.BalanceSpeedModeOptions;

    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
        SessionAnalysisPresentation.VelocityAverageModeOptions;

    public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } =
        SessionAnalysisPresentation.SessionAnalysisTargetProfileOptions;

    public string SessionAnalysisRangeText => AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";

    public string SessionAnalysisModesText => SessionAnalysisPresentation.DescribeModes(
        SelectedTravelHistogramMode,
        SelectedVelocityAverageMode,
        SelectedBalanceDisplacementMode,
        SelectedBalanceSpeedMode);

    public SurfacePresentationState FrontStatisticsState => context.FrontStatisticsState;

    public SurfacePresentationState RearStatisticsState => context.RearStatisticsState;

    public SurfacePresentationState CompressionBalanceState => context.CompressionBalanceState;

    public SurfacePresentationState ReboundBalanceState => context.ReboundBalanceState;

    public SurfacePresentationState FrontForkVibrationState => context.FrontForkVibrationState;

    public SurfacePresentationState FrontFrameVibrationState => context.FrontFrameVibrationState;

    public SurfacePresentationState RearForkVibrationState => context.RearForkVibrationState;

    public SurfacePresentationState RearFrameVibrationState => context.RearFrameVibrationState;

    public SessionDamperPercentages DamperPercentages => context.DamperPercentages;

    public DampingSpeedCutoffs DampingSpeedCutoffs => context.DampingSpeedCutoffs;

    public DampingSpeedCutoffs PlotDampingSpeedCutoffs => context.PlotDampingSpeedCutoffs;

    public bool CanEditDampingSpeedCutoffs => context.CanEditDampingSpeedCutoffs;

    public SessionAnalysisResult SessionAnalysis => context.SessionAnalysis;

    public IRelayCommand<TelemetryRangeSelection?> SelectTelemetryRangeSelectionCommand { get; }

    public TelemetryRangeSelection? SelectedFrontRangeSelection => context.SelectedFrontRangeSelection;

    public TelemetryRangeSelection? SelectedRearRangeSelection => context.SelectedRearRangeSelection;

    public void PreviewDampingSpeedCutoff(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        previewDampingSpeedCutoff(side, circuit, cutoffMmPerSecond);
    }

    public void CancelDampingSpeedCutoffPreview()
    {
        cancelDampingSpeedCutoffPreview();
    }

    public Task CommitDampingSpeedCutoffAsync(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        return commitDampingSpeedCutoffAsync(side, circuit, cutoffMmPerSecond);
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null)
        {
            return;
        }

        OnPropertyChanged(args.PropertyName);
        if (args.PropertyName is nameof(RecordedSessionContext.AnalysisRange))
        {
            OnPropertyChanged(nameof(SessionAnalysisRangeText));
            return;
        }

        if (args.PropertyName is nameof(RecordedSessionContext.SelectedTravelHistogramMode)
            or nameof(RecordedSessionContext.SelectedVelocityAverageMode)
            or nameof(RecordedSessionContext.SelectedBalanceDisplacementMode)
            or nameof(RecordedSessionContext.SelectedBalanceSpeedMode))
        {
            OnPropertyChanged(nameof(SessionAnalysisModesText));
        }
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }
}
