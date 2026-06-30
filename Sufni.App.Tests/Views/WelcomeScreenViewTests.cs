using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Tests.TestSupport;

using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Tests.Views;

[Collection("Ui")]
public class WelcomeScreenViewTests
{
    [AvaloniaFact]
    public async Task WelcomeScreenView_ShowsLogsButton_WhenCapabilityAvailable()
    {
        await using var mounted = await MountAsync(CreateViewModel(canOpenLogsFolder: true));
        Assert.True(mounted.View.FindControl<Button>("OpenLogsFolderButton")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task WelcomeScreenView_HidesLogsButton_WhenCapabilityMissing()
    {
        await using var mounted = await MountAsync(CreateViewModel(canOpenLogsFolder: false));
        Assert.False(mounted.View.FindControl<Button>("OpenLogsFolderButton")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task WelcomeScreenView_WiresButtonsToCommands()
    {
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        var filesService = Substitute.For<IFilesService>();

        filesService.CanOpenLogsFolder.Returns(true);
        filesService.OpenLogsFolderAsync().Returns(Task.CompletedTask);

        var viewModel = new WelcomeScreenViewModel(shell, dialogService, bikeCoordinator, setupCoordinator, importSessionsCoordinator, filesService, new InlineUiThreadDispatcher());

        await using var mounted = await MountAsync(viewModel);

        var importButton = mounted.View.FindControl<Button>("ImportSessionsButton");
        var addBikeButton = mounted.View.FindControl<Button>("AddBikeButton");
        var addSetupButton = mounted.View.FindControl<Button>("AddSetupButton");
        var logsButton = mounted.View.FindControl<Button>("OpenLogsFolderButton");

        Assert.NotNull(importButton);
        Assert.NotNull(addBikeButton);
        Assert.NotNull(addSetupButton);
        Assert.NotNull(logsButton);

        importButton!.Command!.Execute(importButton.CommandParameter);
        addBikeButton!.Command!.Execute(addBikeButton.CommandParameter);
        addSetupButton!.Command!.Execute(addSetupButton.CommandParameter);
        logsButton!.Command!.Execute(logsButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await importSessionsCoordinator.Received(1).OpenAsync();
        await bikeCoordinator.Received(1).OpenCreateAsync();
        await setupCoordinator.Received(1).OpenCreateForDetectedBoardAsync();
        await filesService.Received(1).OpenLogsFolderAsync();
    }

    private static WelcomeScreenViewModel CreateViewModel(bool canOpenLogsFolder = true)
    {
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        var filesService = Substitute.For<IFilesService>();
        filesService.CanOpenLogsFolder.Returns(canOpenLogsFolder);
        filesService.OpenLogsFolderAsync().Returns(Task.CompletedTask);
        return new WelcomeScreenViewModel(shell, dialogService, bikeCoordinator, setupCoordinator, importSessionsCoordinator, filesService, new InlineUiThreadDispatcher());
    }

    private static async Task<MountedWelcomeScreenView> MountAsync(WelcomeScreenViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new WelcomeScreenView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedWelcomeScreenView(host, view);
    }
}

internal sealed class MountedWelcomeScreenView(Window host, WelcomeScreenView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public WelcomeScreenView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
