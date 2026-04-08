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

namespace Sufni.App.Tests.Views;

public class ImportSessionsViewTests
{
    [AvaloniaFact]
    public async Task ImportSessionsView_DisablesEditorsWhileImportRuns()
    {
        await AssertEditorsDisabledWhileImportRunsAsync(() => new ImportSessionsView());
    }

    [AvaloniaFact]
    public async Task ImportSessionsDesktopView_DisablesEditorsWhileImportRuns()
    {
        await AssertEditorsDisabledWhileImportRunsAsync(() => new ImportSessionsDesktopView());
    }

    private static async Task AssertEditorsDisabledWhileImportRunsAsync(Func<UserControl> createView)
    {
        using var _ = new TestSynchronizationContextScope();
        EnsureImportViewResources();

        var telemetryDataStoreService = Substitute.For<ITelemetryDataStoreService>();
        var filesService = Substitute.For<IFilesService>();
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var setupCoordinator = Substitute.For<ISetupCoordinator>();
        var importSessionsCoordinator = Substitute.For<IImportSessionsCoordinator>();
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
                dataStore,
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

    private static ITelemetryDataStore CreateDataStore(string name = "store", Guid? boardId = null)
    {
        var dataStore = Substitute.For<ITelemetryDataStore>();
        dataStore.Name.Returns(name);
        dataStore.BoardId.Returns(boardId);
        return dataStore;
    }

    private static ITelemetryFile CreateTelemetryFile(string name)
    {
        var telemetryFile = Substitute.For<ITelemetryFile>();
        telemetryFile.Name.Returns(name);
        telemetryFile.FileName.Returns($"{name}.SST");
        telemetryFile.Description.Returns(string.Empty);
        telemetryFile.StartTime.Returns(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        telemetryFile.Duration.Returns("1s");
        telemetryFile.ShouldBeImported.Returns(true);
        return telemetryFile;
    }
}