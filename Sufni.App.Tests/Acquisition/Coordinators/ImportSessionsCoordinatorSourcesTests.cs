using Avalonia.Platform.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Acquisition;
using Sufni.App.Tests.TestSupport.Harness;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetrySources;

namespace Sufni.App.Tests.Acquisition.Coordinators;

public class ImportSessionsCoordinatorSourcesTests
{
    [Fact]
    public void ObserveSources_SharesBrowseAndReleasesEachObserverIndependently()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var firstIssues = new List<DataStoreDiscoveryIssue>();
        var secondIssues = new List<DataStoreDiscoveryIssue>();
        using var first = coordinator.ObserveSources(firstIssues.Add);
        using var second = coordinator.ObserveSources(secondIssues.Add);
        var source = CreateDataStore();
        harness.DataStores.Add(source);
        var issue = new DataStoreDiscoveryIssue(DataStoreDiscoveryFailureReason.Scan, "scan diagnostic");
        harness.TelemetryDataStoreService.DiscoveryIssueReported += Raise.Event<EventHandler<DataStoreDiscoveryIssue>>(harness.TelemetryDataStoreService, issue);

        Assert.Same(source, Assert.Single(coordinator.Sources));
        Assert.Same(issue, Assert.Single(firstIssues));
        Assert.Same(issue, Assert.Single(secondIssues));
        harness.TelemetryDataStoreService.Received(1).StartBrowse();
        first.Dispose();
        harness.TelemetryDataStoreService.DidNotReceive().StopBrowse();
        second.Dispose();
        harness.TelemetryDataStoreService.Received(1).StopBrowse();
        harness.TelemetryDataStoreService.DiscoveryIssueReported += Raise.Event<EventHandler<DataStoreDiscoveryIssue>>(harness.TelemetryDataStoreService, issue);
        Assert.Single(firstIssues);
        Assert.Single(secondIssues);
    }

    [Fact]
    public void ObserveSources_FailedStartReportsFailureAndAllowsNextObservation()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var fail = true;
        harness.TelemetryDataStoreService.When(service => service.StartBrowse()).Do(_ =>
        {
            if (fail) throw new IOException("browse diagnostic");
        });
        var issues = new List<DataStoreDiscoveryIssue>();
        using var failed = coordinator.ObserveSources(issues.Add);
        Assert.Equal(DataStoreDiscoveryFailureReason.Browse, Assert.Single(issues).Reason);
        Assert.Contains("browse diagnostic", issues[0].Details);
        harness.TelemetryDataStoreService.Received(1).StopBrowse();

        fail = false;
        using var retry = coordinator.ObserveSources(issues.Add);

        harness.TelemetryDataStoreService.Received(2).StartBrowse();
        Assert.Single(issues);
    }

    [Fact]
    public void ObserveSources_StopFailureReportsDiagnosticsWithoutThrowingFromLeaseDisposal()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        harness.TelemetryDataStoreService.When(service => service.StopBrowse())
            .Do(_ => throw new IOException("stop diagnostic"));
        var issues = new List<DataStoreDiscoveryIssue>();
        var observation = coordinator.ObserveSources(issues.Add);

        observation.Dispose();
        observation.Dispose();

        var issue = Assert.Single(issues);
        Assert.Equal(DataStoreDiscoveryFailureReason.Browse, issue.Reason);
        Assert.Contains("stop diagnostic", issue.Details);
        harness.TelemetryDataStoreService.Received(1).StopBrowse();
    }

    [Fact]
    public async Task LoadFilesAsync_ReturnsClosedFailureWithDiagnostics()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var source = CreateDataStore();
        harness.TelemetryDataStoreService.LoadFilesAsync(source, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("listing diagnostic"));

        var result = await coordinator.LoadFilesAsync(source, TestContext.Current.CancellationToken);

        var failed = Assert.IsType<TelemetryFileListing.Failed>(result);
        Assert.Equal(ImportSourceFailureReason.ListFiles, failed.Reason);
        Assert.Contains("listing diagnostic", failed.Details);
    }

    [Fact]
    public async Task StopAsync_CancelsListingAndWaitsForItsCleanup()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var source = CreateDataStore();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.TelemetryDataStoreService.LoadFilesAsync(source, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            started.SetResult();
            try { return await new TaskCompletionSource<TelemetryFileListing>().Task.WaitAsync(call.Arg<CancellationToken>()); }
            catch (OperationCanceledException)
            {
                canceled.SetResult();
                await cleanup.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
                throw;
            }
        });
        var listing = coordinator.LoadFilesAsync(source, TestContext.Current.CancellationToken);
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var stop = coordinator.StopAsync();
        await canceled.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(stop.IsCompleted);
        cleanup.SetResult();
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.True(listing.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenFolderAsync_UsesFolderCapabilityIndependentlyOfFileImport(bool canPickFolder)
    {
        var harness = new ImportWorkflowHarness();
        var environment = TestAppEnvironment.Create(UiLayoutProfile.Workspace);
        environment = TestAppEnvironment.Create(UiLayoutProfile.Workspace,
            environment.Capabilities with { SupportsStorageProviderImport = true, CanPickFolder = canPickFolder });
        using var coordinator = harness.CreateCoordinator(environment: environment);
        var folder = Substitute.For<IStorageFolder>();
        var source = CreateDataStore();
        harness.FilesService.OpenDataStoreFolderAsync().Returns(Task.FromResult<IStorageFolder?>(folder));
        harness.TelemetryDataStoreService.TryAddStorageProviderAsync(folder, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StorageProviderRegistrationResult>(new StorageProviderRegistrationResult.Added(source)));

        var result = await coordinator.OpenFolderAsync(TestContext.Current.CancellationToken);

        if (canPickFolder) Assert.Same(source, Assert.IsType<StorageProviderRegistrationResult.Added>(result).DataStore);
        else
        {
            Assert.IsType<StorageProviderRegistrationResult.Unavailable>(result);
            await harness.FilesService.DidNotReceive().OpenDataStoreFolderAsync();
            await harness.TelemetryDataStoreService.DidNotReceive().TryAddStorageProviderAsync(Arg.Any<IStorageFolder>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenFolderAsync_ReportsPickerAndRegistrationFailures(bool pickerFails)
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var folder = Substitute.For<IStorageFolder>();
        harness.FilesService.OpenDataStoreFolderAsync().Returns(Task.FromResult<IStorageFolder?>(folder));
        if (pickerFails) harness.FilesService.OpenDataStoreFolderAsync().ThrowsAsync(new IOException("folder diagnostic"));
        else harness.TelemetryDataStoreService.TryAddStorageProviderAsync(folder, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("folder diagnostic"));

        var failed = Assert.IsType<StorageProviderRegistrationResult.Failed>(await coordinator.OpenFolderAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ImportSourceFailureReason.OpenFolder, failed.Reason);
        Assert.Contains("folder diagnostic", failed.Details);
    }

    [Fact]
    public async Task OpenFolderAsync_CanceledPickerDoesNotRegisterSource()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();

        Assert.IsType<StorageProviderRegistrationResult.Canceled>(await coordinator.OpenFolderAsync(TestContext.Current.CancellationToken));

        await harness.TelemetryDataStoreService.DidNotReceive().TryAddStorageProviderAsync(Arg.Any<IStorageFolder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WaitsForOpenPickerAndRejectsItsLateSelection()
    {
        var harness = new ImportWorkflowHarness();
        using var coordinator = harness.CreateCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = new TaskCompletionSource<IStorageFolder?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.FilesService.OpenDataStoreFolderAsync().Returns(_ => { started.SetResult(); return picker.Task; });
        var opening = coordinator.OpenFolderAsync(TestContext.Current.CancellationToken);
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var stop = coordinator.StopAsync();
        Assert.False(stop.IsCompleted);
        picker.SetResult(Substitute.For<IStorageFolder>());
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        await harness.TelemetryDataStoreService.DidNotReceive().TryAddStorageProviderAsync(Arg.Any<IStorageFolder>(), Arg.Any<CancellationToken>());
    }
}
