using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Analysis.Views.Controls;

[Collection("Ui")]
public class TravelAnalysisHostTests
{
    [AvaloniaFact]
    public async Task TravelAnalysisHost_ActivatesFrequencyDemandOnlyWhileFrequencyDistributionIsShown()
    {
        var view = new TravelAnalysisHost
        {
            IsAnalysisDemandActive = true,
            PresentationState = SurfacePresentationState.Ready,
            ShowFrequencyDistribution = false,
            SuspensionType = SuspensionType.Front,
            Telemetry = TestTelemetryData.CreateProcessed(),
        };
        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            await ViewTestHelpers.FlushDispatcherAsync();
            var frequencyPlot = view.GetLogicalDescendants()
                .OfType<AnalysisPlotView>()
                .Single(plot => plot.AnalysisPlotKind == AnalysisPlotKind.TravelFrequencyDistribution);

            Assert.False(frequencyPlot.IsAnalysisDemandActive);

            view.ShowFrequencyDistribution = true;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.True(frequencyPlot.IsAnalysisDemandActive);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
