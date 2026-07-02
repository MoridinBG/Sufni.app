using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sufni.App.Sessions.Insights.Views.Controls;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Insights.Views.Controls;

[Collection("Ui")]
public class SessionInsightsControlsTests
{
    [AvaloniaFact]
    public async Task SessionInsightsStepView_SurfacesMetricsAndFindings()
    {
        var step = new SessionInsightsStep(
            SessionInsightsStepId.Fork,
            "Fork",
            SessionInsightsSeverity.Watch,
            true,
            [new SessionInsightsMetric("Reb 95th", "980", "mm/s", "Fork", "1800-2500 mm/s")],
            [new SessionInsightsFinding(
                SessionInsightsFindingId.ReboundSlowForProfileContext,
                SessionInsightsCategory.ForkDamping,
                SessionInsightsSeverity.Watch,
                SessionInsightsConfidence.Medium,
                "Fork rebound is slow",
                "The fork springs back slower than enduro riding usually wants.",
                "unused fallback",
                [],
                [new Adjustment(
                    AdjustmentComponent.HighSpeedRebound,
                    AdjustmentDirection.Open,
                    "a click at a time",
                    "Fork",
                    "The wheel should return to the ground a bit sooner.",
                    1)])]);

        var view = new SessionInsightsStepView { DataContext = step };
        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var metrics = view.FindControl<ItemsControl>("StepMetricsItemsControl");
            var findings = view.FindControl<ItemsControl>("StepFindingsItemsControl");

            Assert.NotNull(metrics);
            Assert.Same(step.Metrics, metrics!.ItemsSource);

            // Findings are surfaced directly (not tucked behind an expander).
            Assert.NotNull(findings);
            Assert.True(findings!.IsVisible);
            Assert.Same(step.Findings, findings.ItemsSource);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SessionInsightsStepView_ShowsGatingMessage_WhenStepIsGated()
    {
        var step = new SessionInsightsStep(
            SessionInsightsStepId.Rear,
            "Rear",
            SessionInsightsSeverity.Watch,
            true,
            [],
            [])
        {
            GatingMessage = "Sort out “Sag & travel use” first; its changes usually shift these numbers.",
        };

        var view = new SessionInsightsStepView { DataContext = step };
        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var gating = view.FindControl<TextBlock>("GatingMessageTextBlock");
            Assert.NotNull(gating);
            Assert.True(gating!.IsVisible);
            Assert.Equal(step.GatingMessage, gating.Text);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SessionInsightsNextStepCard_RendersExperimentText()
    {
        var nextStep = new SessionInsightsNextStep(
            "Fork",
            new Adjustment(
                AdjustmentComponent.HighSpeedRebound,
                AdjustmentDirection.Open,
                "a click at a time",
                "Fork",
                "The wheel should return to the ground a bit sooner.",
                1),
            "The fork is riding deep while rebound is slow.");

        var view = new SessionInsightsNextStepCard { DataContext = nextStep };
        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var experiment = view.FindControl<TextBlock>("NextStepExperimentTextBlock");
            Assert.NotNull(experiment);
            Assert.Equal(nextStep.Adjustment.ExperimentText, experiment!.Text);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SessionInsightsStepView_HidesFindingsList_WhenStepHasNoFindings()
    {
        var step = new SessionInsightsStep(
            SessionInsightsStepId.Balance,
            "Balance",
            SessionInsightsSeverity.Info,
            false,
            [new SessionInsightsMetric("Compression slope delta", "4.0", "%", null, "< 10 %")],
            []);

        var view = new SessionInsightsStepView { DataContext = step };
        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var findings = view.FindControl<ItemsControl>("StepFindingsItemsControl");
            Assert.NotNull(findings);
            Assert.False(findings!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
