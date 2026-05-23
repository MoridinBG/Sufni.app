using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class SessionAnalysisControlsTests
{
    [AvaloniaFact]
    public async Task SessionAnalysisStepView_RendersMetricsAndPrimaryExperiment_AndHidesEmptyExpanders()
    {
        var step = new SessionAnalysisStep(
            SessionAnalysisStepId.Fork,
            "Fork",
            SessionAnalysisSeverity.Watch,
            true,
            [new SessionAnalysisMetric("Reb 95th", "980", "mm/s", "Fork", "1800-2500 mm/s")],
            new Adjustment(
                AdjustmentComponent.HighSpeedRebound,
                AdjustmentDirection.Open,
                "1 click",
                "Fork",
                "Rebound 95th should rise into 1800-2500 mm/s.",
                1),
            [],
            []);

        var view = new SessionAnalysisStepView { DataContext = step };
        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var primary = view.FindControl<Border>("PrimaryExperimentCard");
            var metrics = view.FindControl<ItemsControl>("StepMetricsItemsControl");
            var otherOptions = view.FindControl<Expander>("OtherOptionsExpander");
            var findings = view.FindControl<Expander>("FindingsExpander");

            Assert.NotNull(primary);
            Assert.True(primary!.IsVisible);
            Assert.NotNull(metrics);
            Assert.Same(step.Metrics, metrics!.ItemsSource);
            Assert.NotNull(otherOptions);
            Assert.False(otherOptions!.IsVisible);
            Assert.NotNull(findings);
            Assert.False(findings!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SessionAnalysisStepView_RendersContextMessage_WhenFindingsHaveNoPrimaryAdjustment()
    {
        var step = new SessionAnalysisStep(
            SessionAnalysisStepId.Balance,
            "Balance",
            SessionAnalysisSeverity.Watch,
            true,
            [new SessionAnalysisMetric("Compression slope delta", "18.0", "%", null, "< 10 %")],
            null,
            [],
            [new SessionAnalysisFinding(
                SessionAnalysisCategory.Balance,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Medium,
                "Compression balance slopes diverge",
                "The front compression trend is steeper than the rear trend.",
                "Resolve travel use / speed context before balance tuning.",
                [])]);

        var view = new SessionAnalysisStepView { DataContext = step };
        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var primary = view.FindControl<Border>("PrimaryExperimentCard");
            var context = view.FindControl<TextBlock>("ContextMessageTextBlock");
            var findings = view.FindControl<Expander>("FindingsExpander");

            Assert.NotNull(primary);
            Assert.False(primary!.IsVisible);
            Assert.NotNull(context);
            Assert.True(context!.IsVisible);
            Assert.Equal("Resolve travel use / speed context before balance tuning.", context.Text);
            Assert.NotNull(findings);
            Assert.True(findings!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

}
