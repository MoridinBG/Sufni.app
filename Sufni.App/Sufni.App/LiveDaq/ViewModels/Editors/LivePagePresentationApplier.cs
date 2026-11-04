using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.ViewModels.Editors;

internal sealed class LivePagePresentationApplier : IAsyncDisposable
{
    private readonly LiveSessionAnalysisWorkspaceViewModel workspace;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly Func<bool> isForeground;
    private readonly Action<FailurePresentation> reportError;
    private readonly string identityKey;
    private readonly SpringPageViewModel springPage;
    private readonly DampingPageViewModel dampingPage;
    private readonly BalancePageViewModel balancePage;
    private readonly NotesPageViewModel notesPage;
    private readonly ObservableCollection<PageViewModelBase> pages;
    private SessionPresentationDimensions? dimensions;
    private TelemetryData? lastBakedTelemetryData;
    private CancellationTokenSource? bakeCts;
    private readonly List<Task> bakeTasks = [];

    public LivePagePresentationApplier(LiveSessionAnalysisWorkspaceViewModel workspace,
        ISessionPresentationService presentation, IBackgroundTaskRunner runner, IUiThreadDispatcher dispatcher,
        Func<bool> isForeground, Action<FailurePresentation> reportError, string identityKey,
        SpringPageViewModel spring, DampingPageViewModel damping, BalancePageViewModel balance,
        NotesPageViewModel notes, ObservableCollection<PageViewModelBase> pages)
    {
        this.workspace = workspace;
        sessionPresentationService = presentation;
        backgroundTaskRunner = runner;
        this.dispatcher = dispatcher;
        this.isForeground = isForeground;
        this.reportError = reportError;
        this.identityKey = identityKey;
        springPage = spring;
        dampingPage = damping;
        balancePage = balance;
        notesPage = notes;
        this.pages = pages;
        workspace.PropertyChanged += OnWorkspaceChanged;
    }

    public void SetDimensions(SessionPresentationDimensions value) => dimensions = value;

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(workspace.PlotDampingSpeedCutoffs)) lastBakedTelemetryData = null;
        if (args.PropertyName is nameof(workspace.PlotDampingSpeedCutoffs) or nameof(workspace.TelemetryData)) Refresh();
    }

    public void Suspend()
    {
        CancelBake();
        lastBakedTelemetryData = null;
    }

    public async ValueTask DisposeAsync()
    {
        workspace.PropertyChanged -= OnWorkspaceChanged;
        Suspend();
        await Task.WhenAll(bakeTasks);
    }

    public void Refresh()
    {
        dampingPage.ApplyDampingPercentages(workspace.DampingPercentages);
        var telemetryData = workspace.TelemetryData;
        if (telemetryData is null)
        {
            Clear();
            return;
        }
        if (!isForeground() ||
            this.dimensions is not { } dimensions)
        {
            return;
        }

        if (ReferenceEquals(lastBakedTelemetryData, telemetryData))
        {
            return;
        }

        lastBakedTelemetryData = telemetryData;
        CancelBake();
        var cts = new CancellationTokenSource();
        bakeCts = cts;
        var service = sessionPresentationService;
        var cutoffs = workspace.PlotDampingSpeedCutoffs;

        bakeTasks.RemoveAll(task => task.IsCompleted);
        bakeTasks.Add(BakeCachePresentationAsync(service, telemetryData, dimensions, cts, cutoffs));
    }

    private async Task BakeCachePresentationAsync(
        ISessionPresentationService service,
        TelemetryData telemetryData,
        SessionPresentationDimensions dimensions,
        CancellationTokenSource cts,
        DampingSpeedCutoffs cutoffs)
    {
        try
        {
            var data = await backgroundTaskRunner.RunAsync(
                () => service.BuildCachePresentation(telemetryData, dimensions, cts.Token, cutoffs),
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            dispatcher.Post(() =>
            {
                if (cts.IsCancellationRequested || !ReferenceEquals(bakeCts, cts) || !isForeground())
                {
                    return;
                }

                ApplyCachePresentation(data);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.ForContext<LivePagePresentationApplier>().Warning(e, "Live analysis rendering failed for {IdentityKey}", identityKey);
            var failure = new FailurePresentation("Live analysis could not be rendered. Capture continues.", e.ToString());
            dispatcher.Post(() =>
            {
                if (!cts.IsCancellationRequested && ReferenceEquals(bakeCts, cts) && isForeground())
                    reportError(failure);
            });
        }
        finally { cts.Dispose(); }
    }

    private void CancelBake()
    {
        var cts = bakeCts;
        bakeCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

    }

    private void ApplyCachePresentation(SessionCachePresentationData data)
    {
        var hasFrontTravelDistribution = !string.IsNullOrWhiteSpace(data.FrontTravelDistribution);
        var hasRearTravelDistribution = !string.IsNullOrWhiteSpace(data.RearTravelDistribution);
        var hasFrontVelocityDistribution = !string.IsNullOrWhiteSpace(data.FrontVelocityDistribution);
        var hasRearVelocityDistribution = !string.IsNullOrWhiteSpace(data.RearVelocityDistribution);
        var hasCompressionBalance = !string.IsNullOrWhiteSpace(data.CompressionBalance);
        var hasReboundBalance = !string.IsNullOrWhiteSpace(data.ReboundBalance);

        var frontAnalysis = workspace.FrontAnalysisState;
        var rearAnalysis = workspace.RearAnalysisState;
        var compressionBalance = workspace.CompressionBalanceState;
        var reboundBalance = workspace.ReboundBalanceState;

        springPage.FrontTravelDistribution = data.FrontTravelDistribution;
        springPage.RearTravelDistribution = data.RearTravelDistribution;
        springPage.FrontDistributionState = ResolveSurfaceState(hasFrontTravelDistribution, frontAnalysis);
        springPage.RearDistributionState = ResolveSurfaceState(hasRearTravelDistribution, rearAnalysis);

        dampingPage.FrontVelocityDistribution = data.FrontVelocityDistribution;
        dampingPage.RearVelocityDistribution = data.RearVelocityDistribution;
        dampingPage.FrontDistributionState = ResolveSurfaceState(hasFrontVelocityDistribution, frontAnalysis);
        dampingPage.RearDistributionState = ResolveSurfaceState(hasRearVelocityDistribution, rearAnalysis);

        dampingPage.ApplyDampingPercentages(workspace.DampingPercentages);

        balancePage.CompressionBalance = data.CompressionBalance;
        balancePage.ReboundBalance = data.ReboundBalance;
        balancePage.CompressionBalanceState = ResolveSurfaceState(hasCompressionBalance, compressionBalance);
        balancePage.ReboundBalanceState = ResolveSurfaceState(hasReboundBalance, reboundBalance);

        var balancePageVisible = data.BalanceAvailable
            || compressionBalance.ReservesLayout
            || reboundBalance.ReservesLayout;
        EnsureBalancePage(balancePageVisible);
    }

    private static SurfacePresentationState ResolveSurfaceState(bool svgPresent, SurfacePresentationState workspaceState)
    {
        if (svgPresent)
        {
            return SurfacePresentationState.Ready;
        }

        return workspaceState.IsHidden ? SurfacePresentationState.Hidden : workspaceState;
    }

    private void EnsureBalancePage(bool balanceAvailable)
    {
        var containsBalancePage = pages.Contains(balancePage);
        if (balanceAvailable)
        {
            if (containsBalancePage)
            {
                return;
            }

            var notesIndex = pages.IndexOf(notesPage);
            if (notesIndex < 0)
            {
                pages.Add(balancePage);
            }
            else
            {
                pages.Insert(notesIndex, balancePage);
            }

            return;
        }

        if (containsBalancePage)
        {
            pages.Remove(balancePage);
        }
    }

    public void Clear()
    {
        Suspend();
        springPage.FrontTravelDistribution = null;
        springPage.RearTravelDistribution = null;
        springPage.FrontDistributionState = SurfacePresentationState.Hidden;
        springPage.RearDistributionState = SurfacePresentationState.Hidden;

        dampingPage.FrontVelocityDistribution = null;
        dampingPage.RearVelocityDistribution = null;
        dampingPage.FrontDistributionState = SurfacePresentationState.Hidden;
        dampingPage.RearDistributionState = SurfacePresentationState.Hidden;
        dampingPage.ClearDampingPercentages();

        balancePage.CompressionBalance = null;
        balancePage.ReboundBalance = null;
        balancePage.CompressionBalanceState = SurfacePresentationState.Hidden;
        balancePage.ReboundBalanceState = SurfacePresentationState.Hidden;

        EnsureBalancePage(balanceAvailable: false);
    }

}
