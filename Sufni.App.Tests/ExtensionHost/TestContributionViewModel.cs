using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.Tests.ExtensionHost;

internal sealed class TestContributionViewModel :
    ContentControl,
    IAppToolbarContributionViewModel,
    IRecordedSessionToolbarContributionViewModel,
    IRecordedSessionPageContributionViewModel,
    IRecordedSessionMediaPaneContributionViewModel,
    IRecordedSessionStatisticsBannerContributionViewModel,
    IRecordedSessionStatisticsTabContributionViewModel,
    IRecordedSessionStatisticsOverlayContributionViewModel,
    IRecordedSessionListIndicatorContributionViewModel,
    IRecordedSessionListActionContributionViewModel,
    IRecordedSessionHostedGraphRowContributionViewModel;
