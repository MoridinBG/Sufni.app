using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class TestContributionViewModel :
    ContentControl,
    IAppToolbarContributionViewModel,
    IRecordedSessionToolbarContributionViewModel,
    IRecordedSessionPageContributionViewModel,
    IRecordedSessionMediaPaneContributionViewModel,
    IRecordedSessionAnalysisBannerContributionViewModel,
    IRecordedSessionAnalysisTabContributionViewModel,
    IRecordedSessionAnalysisOverlayContributionViewModel,
    IRecordedSessionListIndicatorContributionViewModel,
    IRecordedSessionListActionContributionViewModel,
    IRecordedSessionHostedSignalRowContributionViewModel;
