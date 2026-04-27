using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views;

namespace Sufni.App.Tests.Views;

public class WelcomeScreenViewTests
{
    [AvaloniaFact]
    public async Task WelcomeScreenView_ShowsLogsButton_OnDesktopOnly()
    {
        TestApp.SetIsDesktop(true);
        await using var desktopMounted = await MountAsync(CreateViewModel());
        Assert.True(desktopMounted.View.FindControl<Button>("OpenLogsFolderButton")!.IsVisible);

        desktopMounted.Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();

        TestApp.SetIsDesktop(false);
        await using var mobileMounted = await MountAsync(CreateViewModel());
        Assert.False(mobileMounted.View.FindControl<Button>("OpenLogsFolderButton")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task WelcomeScreenView_WiresButtonsToCommands()
    {
        TestApp.SetIsDesktop(true);

        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        var filesService = Substitute.For<IFilesService>();

        filesService.OpenLogsFolderAsync().Returns(Task.CompletedTask);

        var viewModel = new WelcomeScreenViewModel(shell, dialogService, bikeCoordinator, setupCoordinator, importSessionsCoordinator, filesService);

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

    private static WelcomeScreenViewModel CreateViewModel()
    {
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var importSessionsCoordinator = TestCoordinatorSubstitutes.ImportSessions();
        var filesService = Substitute.For<IFilesService>();
        filesService.OpenLogsFolderAsync().Returns(Task.CompletedTask);
        return new WelcomeScreenViewModel(shell, dialogService, bikeCoordinator, setupCoordinator, importSessionsCoordinator, filesService);
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