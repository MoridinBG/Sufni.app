using System.Reactive.Subjects;
using Sufni.App.LiveDaq.Services.LiveStreaming;
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
    public async Task SourceMessages_ShowEmptyAndBusyStatesAndGateImport()
    {
        var harness = new ImportWorkflowHarness();
        var vm = harness.CreateViewModel();
        await using var mounted = await MountAsync(vm);
        var empty = Assert.Single(mounted.View.GetVisualDescendants().OfType<TextBlock>(), block => block.Name == "NoDaqMessage");
        var busy = Assert.Single(mounted.View.GetVisualDescendants().OfType<TextBlock>(), block => block.Name == "DaqBusyMessage");
        Assert.True(empty.IsVisible);
        Assert.False(busy.IsVisible);
        var source = CreateDataStore();
        using var transport = new BehaviorSubject<DaqTransportHolder?>(null);
        source.ObserveTransport().Returns(transport);

        harness.DataStores.Add(source);
        vm.SelectedSetup = Guid.NewGuid();
        transport.OnNext(new DaqTransportHolder.Streaming());
        await ViewTestHelpers.FlushDispatcherAsync();
        var importButton = Assert.Single(mounted.View.GetVisualDescendants().OfType<Button>(), button => ReferenceEquals(button.Command, vm.ImportSessionsCommand));
        Assert.False(empty.IsVisible);
        Assert.True(busy.IsVisible);
        Assert.False(importButton.Command!.CanExecute(importButton.CommandParameter));
        transport.OnNext(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(busy.IsVisible);
        Assert.True(importButton.Command!.CanExecute(importButton.CommandParameter));
        harness.DataStores.Clear();
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(empty.IsVisible);
    }

    [AvaloniaFact]
    public async Task ImportSessionsView_DisablesEditors_WhenAnotherTabStartedImport()
    {
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        harness.SetupQuery.Add(TestSnapshots.Setup(), boardId);
        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("lap");
        harness.DataStores.Add(dataStore);
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([.. new[] { file }], null)));
        var viewModel = harness.CreateViewModel();

        await using var mounted = await MountAsync(viewModel);

        Assert.Single(mounted.View.GetVisualDescendants().OfType<ImportSessionsContentView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<ImportSessionsActionRow>());
        Assert.Equal([file], viewModel.TelemetryFiles.Select(row => row.File));

        var expander = mounted.View.GetVisualDescendants().OfType<Expander>().First();
        expander.IsExpanded = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        harness.ImportState.OnNext(new SessionImportState.Running(1, 2, []));
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

        harness.ImportState.OnNext(new SessionImportState.Completed(new SessionImportResult([], []), []));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(importChoice.IsEnabled);
        Assert.False(busyOverlay.IsActive);
    }

    [AvaloniaFact]
    public async Task ActionSelector_MapsUserChoicesToImportTrashAndIgnore()
    {
        var harness = new ImportWorkflowHarness();
        var dataStore = CreateDataStore();
        var file = CreateTelemetryFile("lap");
        harness.DataStores.Add(dataStore);
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([file], null)));
        var viewModel = harness.CreateViewModel();
        await using var mounted = await MountAsync(viewModel);
        var selector = Assert.Single(mounted.View.GetLogicalDescendants().OfType<ComboBox>(),
            combo => combo.Name == "ActionSelector");
        Assert.Equal(0, selector.SelectedIndex);

        selector.SelectedIndex = 1;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(ImportFileAction.Import, Assert.Single(viewModel.TelemetryFiles).Action);

        selector.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(ImportFileAction.Trash, Assert.Single(viewModel.TelemetryFiles).Action);

        selector.SelectedIndex = 0;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(ImportFileAction.Ignore, Assert.Single(viewModel.TelemetryFiles).Action);
    }

    [AvaloniaFact]
    public async Task ImportSessionsView_ShowsMalformedFileReason()
    {
        var harness = new ImportWorkflowHarness();
        var reason = "trailing chunk was trimmed";
        var dataStore = CreateDataStore();
        var file = CreateTelemetryFile(
            "trimmed",
            malformedMessage: reason,
            canImport: true);
        harness.DataStores.Add(dataStore);
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([.. new[] { file }], null)));
        var viewModel = harness.CreateViewModel();

        await using var mounted = await MountAsync(viewModel);

        var label = mounted.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .First(textBlock => textBlock.Text == "(Malformed)");
        Assert.Equal(reason, ToolTip.GetTip(label));
    }

    [AvaloniaFact]
    public async Task ImportSessionsView_DisplaysRecordingStartInLocalTime()
    {
        var harness = new ImportWorkflowHarness();
        var store = CreateDataStore();
        var startUtc = new DateTime(2025, 6, 1, 12, 34, 0, DateTimeKind.Utc);
        var file = CreateTelemetryFile("lap", startTime: startUtc);
        harness.DataStores.Add(store);
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(store, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([file], null)));

        await using var mounted = await MountAsync(harness.CreateViewModel());

        var timestamp = Assert.Single(mounted.View.GetLogicalDescendants()
            .OfType<TextBlock>(), text => text.Name == "FileStartTimeText");
        Assert.Equal(startUtc.ToLocalTime().ToString(" yyyy-MM-dd HH:mm"), timestamp.Text);
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
