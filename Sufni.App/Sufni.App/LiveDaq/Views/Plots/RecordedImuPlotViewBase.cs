using System;
using Avalonia;
using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Views.Plots;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Views.Plots;

public abstract class RecordedImuPlotViewBase : SufniTelemetryPlotView
{
    private IRecordedSessionAnalysisResultState? subscribedAnalysisResultState;
    private IDisposable? analysisInputSubscription;
    private IDisposable? analysisResultSubscription;
    private RecordedSessionAnalysisKey? projectionKey;
    private bool isAttachedToVisualTree;

    public static readonly StyledProperty<IRecordedSessionAnalysisResultState?> AnalysisResultStateProperty =
        AvaloniaProperty.Register<RecordedImuPlotViewBase, IRecordedSessionAnalysisResultState?>(
            nameof(AnalysisResultState));

    public IRecordedSessionAnalysisResultState? AnalysisResultState
    {
        get => GetValue(AnalysisResultStateProperty);
        set => SetValue(AnalysisResultStateProperty, value);
    }

    protected RecordedImuPlotViewBase()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.Property != AnalysisResultStateProperty)
            {
                return;
            }

            SubscribeToAnalysisResultState(
                isAttachedToVisualTree ? AnalysisResultState : null);
            if (AnalysisResultState is not null)
            {
                ReloadTelemetry();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        isAttachedToVisualTree = true;
        SubscribeToAnalysisResultState(AnalysisResultState);
        base.OnAttachedToVisualTree(args);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        isAttachedToVisualTree = false;
        SubscribeToAnalysisResultState(null);
        base.OnDetachedFromVisualTree(args);
    }

    protected sealed override void LoadPlotData(TelemetryPlot plotModel)
    {
        if (Telemetry is not { } telemetry ||
            AnalysisResultState is not { } state ||
            state.CurrentInputs is not { } inputs)
        {
            return;
        }

        var key = inputs.ImuDisplayProjectionKey;
        switch (state.Get(key))
        {
            case ImuDisplayProjectionAnalysisResult result:
                LoadProjection(plotModel, telemetry, result.Projection);
                break;
            case null:
                _ = state.RequestAsync(key);
                break;
            default:
                throw new InvalidOperationException("The recorded IMU projection key returned an unexpected result type.");
        }
    }

    protected abstract void LoadProjection(
        TelemetryPlot plotModel,
        TelemetryData telemetry,
        RecordedImuDisplaySeries projection);

    private void SubscribeToAnalysisResultState(IRecordedSessionAnalysisResultState? state)
    {
        if (ReferenceEquals(subscribedAnalysisResultState, state))
        {
            return;
        }

        analysisInputSubscription?.Dispose();
        analysisInputSubscription = null;
        analysisResultSubscription?.Dispose();
        analysisResultSubscription = null;
        subscribedAnalysisResultState = state;
        projectionKey = state?.CurrentInputs?.ImuDisplayProjectionKey;

        if (state is null)
        {
            return;
        }

        analysisInputSubscription = state.ConnectInputs().Subscribe(OnAnalysisInputsChanged);
        analysisResultSubscription = state.Connect().Subscribe(OnAnalysisResultChanged);
    }

    private void OnAnalysisInputsChanged(RecordedSessionAnalysisInputs inputs)
    {
        var nextKey = inputs.ImuDisplayProjectionKey;
        if (nextKey == projectionKey)
        {
            return;
        }

        projectionKey = nextKey;
        ReloadTelemetry();
    }

    private void OnAnalysisResultChanged(RecordedSessionAnalysisResultChanged change)
    {
        var key = AnalysisResultState?.CurrentInputs?.ImuDisplayProjectionKey;
        if (key is null || change.Key != key || change.Result is not ImuDisplayProjectionAnalysisResult)
        {
            return;
        }

        ReloadTelemetry();
    }
}
