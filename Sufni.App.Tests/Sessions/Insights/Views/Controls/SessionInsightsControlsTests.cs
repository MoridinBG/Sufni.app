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
