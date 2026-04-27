using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views;

public class ImportSessionsViewTests
{
    [AvaloniaFact]
    public async Task ImportSessionsView_DisablesEditors_WhileImportRuns()
    {
        await AssertEditorsDisabledWhileImportRunsAsync(() => new ImportSessionsView());
    }

    [AvaloniaFact]
    public async Task ImportSessionsDesktopView_DisablesEditors_WhileImportRuns()
    {
        await AssertEditorsDisabledWhileImportRunsAsync(() => new ImportSessionsDesktopView());
    }

    [AvaloniaFact]
    public async Task ImportSessionsView_MalformedLabel_ShowsTooltipReason()
    {
        await AssertMalformedLabelTooltipAsync(() => new ImportSessionsView());
    }

    [AvaloniaFact]
    public async Task ImportSessionsDesktopView_MalformedLabel_ShowsTooltipReason()
    {
        await AssertMalformedLabelTooltipAsync(() => new ImportSessionsDesktopView());
    }

    private static async Task AssertEditorsDisabledWhileImportRunsAsync(Func<UserControl> createView)
    {
        using var _ = new TestSynchronizationContextScope();
        EnsureImportViewResources();

        var telemetryDataStoreService = Substitute.For<ITelemetryDataStoreService>();
        var filesService = Substitute.For<IFilesService>();
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        var setupStore = Substitute.For<ISetupStore>();

        var dataStores = new ObservableCollection<ITelemetryDataStore>();
        var setupCache = new SourceCache<SetupSnapshot, Guid>(s => s.Id);

        telemetryDataStoreService.DataStores.Returns(dataStores);
        setupStore.Connect().Returns(setupCache.Connect());
        setupStore.FindByBoardId(Arg.Any<Guid>())
            .Returns(callInfo => setupCache.Items.FirstOrDefault(s => s.BoardId == callInfo.Arg<Guid>()));

        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        setupCache.AddOrUpdate(setup);

        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("lap");
        dataStores.Add(dataStore);

        telemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }),
                Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }));

        var importCompletion = new TaskCompletionSource<SessionImportResult>();
        importSessionsCoordinator.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                setup.Id,
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(importCompletion.Task);

        var viewModel = new ImportSessionsViewModel(
            telemetryDataStoreService,
            filesService,
            shell,
            dialogService,
            setupCoordinator,
            importSessionsCoordinator,
            setupStore);

        var view = createView();
        view.DataContext = viewModel;
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Equal(setup.Id, viewModel.SelectedSetup);
        Assert.Single(viewModel.TelemetryFiles);

        var expander = view.GetVisualDescendants().OfType<Expander>().First();
        expander.IsExpanded = true;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var importTask = viewModel.ImportSessionsCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var toggle = view.GetLogicalDescendants().OfType<ToggleButton>()
            .First(button => button.IsThreeState);
        var textBoxes = view.GetLogicalDescendants().OfType<TextBox>().ToArray();

        Assert.False(toggle.IsEnabled);
        Assert.NotEmpty(textBoxes);
        Assert.All(textBoxes, textBox => Assert.False(textBox.IsEnabled));

        importCompletion.SetResult(new SessionImportResult(
            Array.Empty<SessionSnapshot>(),
            Array.Empty<(string FileName, string ErrorMessage)>()));
        await importTask;

        host.Close();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static async Task AssertMalformedLabelTooltipAsync(Func<UserControl> createView)
    {
        using var _ = new TestSynchronizationContextScope();
        EnsureImportViewResources();

        var telemetryDataStoreService = Substitute.For<ITelemetryDataStoreService>();
        var filesService = Substitute.For<IFilesService>();
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        var setupStore = Substitute.For<ISetupStore>();

        var dataStores = new ObservableCollection<ITelemetryDataStore>();
        var setupCache = new SourceCache<SetupSnapshot, Guid>(s => s.Id);
        var reason = "trailing chunk was trimmed";

        telemetryDataStoreService.DataStores.Returns(dataStores);
        setupStore.Connect().Returns(setupCache.Connect());
        setupStore.FindByBoardId(Arg.Any<Guid>())
            .Returns(callInfo => setupCache.Items.FirstOrDefault(s => s.BoardId == callInfo.Arg<Guid>()));

        var dataStore = CreateDataStore();
        var file = CreateTelemetryFile(
            "trimmed",
            malformedMessage: reason,
            canImport: true);
        dataStores.Add(dataStore);

        telemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }));

        var viewModel = new ImportSessionsViewModel(
            telemetryDataStoreService,
            filesService,
            shell,
            dialogService,
            setupCoordinator,
            importSessionsCoordinator,
            setupStore);

        var view = createView();
        view.DataContext = viewModel;
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Single(viewModel.TelemetryFiles);

        var label = view.GetLogicalDescendants().OfType<TextBlock>()
            .First(textBlock => textBlock.Text == "(Malformed)");

        Assert.Equal(reason, ToolTip.GetTip(label));

        host.Close();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void EnsureImportViewResources()
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        resources["SufniDangerColor"] = Brushes.Red;
        resources["SufniDangerColorDark"] = Brushes.DarkRed;
        resources["SufniRegion"] = Brushes.Gray;
        resources["SufniBorderBrush"] = Brushes.Black;
        resources["SufniAccentColor"] = Brushes.CornflowerBlue;
    }
}