using Avalonia.Controls;
using System;
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

internal sealed class DisposableContributionViewModel :
    IAppToolbarContributionViewModel,
    IRecordedSessionToolbarContributionViewModel,
    IRecordedSessionPageContributionViewModel,
    IRecordedSessionMediaPaneContributionViewModel,
    IRecordedSessionAnalysisBannerContributionViewModel,
    IRecordedSessionAnalysisTabContributionViewModel,
    IRecordedSessionAnalysisOverlayContributionViewModel,
    IRecordedSessionListIndicatorContributionViewModel,
    IRecordedSessionListActionContributionViewModel,
    IRecordedSessionHostedSignalRowContributionViewModel,
    IDisposable
{
    public int DisposeCount { get; private set; }
    public bool IsDisposed => DisposeCount > 0;

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        DisposeCount++;
    }
}
