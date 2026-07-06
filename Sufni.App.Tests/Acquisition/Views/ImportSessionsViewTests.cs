using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using DynamicData;
using NSubstitute;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Views;
using Sufni.App.Acquisition.Views.Shared;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Acquisition;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetrySources;

namespace Sufni.App.Tests.Acquisition.Views;

[Collection("Ui")]
public class ImportSessionsViewTests
{
    [AvaloniaFact]
    public async Task ImportSessionsView_ComposesImportContentAndDisablesEditorsWhileImportRuns()
    {
        using var _ = new TestSynchronizationContextScope();
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        harness.SetupCache.AddOrUpdate(TestSnapshots.Setup(boardId: boardId));
        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("lap");
        harness.DataStores.Add(dataStore);
        harness.TelemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }));
        var importCompletion = new TaskCompletionSource<SessionImportResult>();
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                Arg.Any<Guid>(),
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(importCompletion.Task);
        var viewModel = harness.CreateViewModel();

        await using var mounted = await MountAsync(viewModel);

        Assert.Single(mounted.View.GetVisualDescendants().OfType<ImportSessionsContentView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<ImportSessionsActionRow>());
        Assert.Equal([file], viewModel.TelemetryFiles);

        var expander = mounted.View.GetVisualDescendants().OfType<Expander>().First();
        expander.IsExpanded = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        var importTask = viewModel.ImportSessionsCommand.ExecuteAsync(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        var importChoice = mounted.View.GetLogicalDescendants().OfType<ComboBox>()
            .First(combo => combo.Classes.Contains("importchoice"));
        var textBoxes = mounted.View.GetLogicalDescendants().OfType<TextBox>().ToArray();
        var busyOverlay = mounted.View.GetVisualDescendants().OfType<BusyOverlay>().Single();

        Assert.False(importChoice.IsEnabled);
        Assert.NotEmpty(textBoxes);
        Assert.All(textBoxes, textBox => Assert.False(textBox.IsEnabled));
        Assert.True(busyOverlay.IsActive);
        Assert.False(busyOverlay.ShowTint);
        Assert.True(busyOverlay.ShowMessage);
        Assert.Equal(viewModel.ImportProgressText, busyOverlay.Message);

        importCompletion.SetResult(new SessionImportResult([], []));
        await importTask;
    }

    [AvaloniaFact]
    public async Task ImportSessionsView_ShowsMalformedFileReason()
    {
        using var _ = new TestSynchronizationContextScope();
        var harness = new ImportWorkflowHarness();
        var reason = "trailing chunk was trimmed";
        var dataStore = CreateDataStore();
        var file = CreateTelemetryFile(
            "trimmed",
            malformedMessage: reason,
            canImport: true);
        harness.DataStores.Add(dataStore);
        harness.TelemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }));
        var viewModel = harness.CreateViewModel();

        await using var mounted = await MountAsync(viewModel);

        var label = mounted.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .First(textBlock => textBlock.Text == "(Malformed)");
        Assert.Equal(reason, ToolTip.GetTip(label));
    }

    private static async Task<MountedImportSessionsView> MountAsync(object dataContext)
    {
        EnsureImportViewResources();
        var view = new ImportSessionsView
        {
            DataContext = dataContext,
        };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedImportSessionsView(host, view);
    }

    private static void EnsureImportViewResources()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        resources["SufniDangerColor"] = Brushes.Red;
        resources["SufniDangerColorDark"] = Brushes.DarkRed;
        resources["SufniRegion"] = Brushes.Gray;
        resources["SufniBorderBrush"] = Brushes.Black;
        resources["SufniAccentColor"] = Brushes.CornflowerBlue;
        resources["SufniImportActionImportRowBrush"] = new SolidColorBrush(Colors.CornflowerBlue);
        resources["SufniImportActionTrashRowBrush"] = new SolidColorBrush(Colors.IndianRed);
    }
}

internal sealed record MountedImportSessionsView(Window Host, ImportSessionsView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
