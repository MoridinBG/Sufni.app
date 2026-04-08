using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views;
using Sufni.App.DesktopViews;
using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;

namespace Sufni.App;

public partial class App : Application
{
    public static IServiceCollection ServiceCollection { get; } = new ServiceCollection();

    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }
    public bool IsDesktop { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            ServiceCollection.AddSingleton<IMainWindowShellHost>(sp =>
                sp.GetRequiredService<MainWindowViewModel>());
            ServiceCollection.AddSingleton<IShellCoordinator>(sp =>
                new DesktopShellCoordinator(() => sp.GetRequiredService<IMainWindowShellHost>()));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            ServiceCollection.AddSingleton<IMainViewShellHost>(sp =>
                sp.GetRequiredService<MainViewModel>());
            ServiceCollection.AddSingleton<IShellCoordinator>(sp =>
                new MobileShellCoordinator(() => sp.GetRequiredService<IMainViewShellHost>()));
        }

        ServiceCollection.AddSingleton<IHttpApiService, HttpApiService>();
        ServiceCollection.AddSingleton<ITelemetryDataStoreService, TelemetryDataStoreService>();
        ServiceCollection.AddSingleton<IDatabaseService, SqLiteDatabaseService>();
        ServiceCollection.AddSingleton<IFilesService>(_ => new FilesService());
        ServiceCollection.AddSingleton<IDialogService>(_ => new DialogService());
        ServiceCollection.AddSingleton<BikeStore>();
        ServiceCollection.AddSingleton<IBikeStore>(sp => sp.GetRequiredService<BikeStore>());
        ServiceCollection.AddSingleton<IBikeStoreWriter>(sp => sp.GetRequiredService<BikeStore>());
        ServiceCollection.AddSingleton<IBikeDependencyQuery, BikeDependencyQuery>();
        ServiceCollection.AddSingleton<IBikeCoordinator, BikeCoordinator>();
        ServiceCollection.AddSingleton<SetupStore>();
        ServiceCollection.AddSingleton<ISetupStore>(sp => sp.GetRequiredService<SetupStore>());
        ServiceCollection.AddSingleton<ISetupStoreWriter>(sp => sp.GetRequiredService<SetupStore>());
        ServiceCollection.AddSingleton<ISetupCoordinator, SetupCoordinator>();
        ServiceCollection.AddSingleton<SessionStore>();
        ServiceCollection.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SessionStore>());
        ServiceCollection.AddSingleton<ISessionStoreWriter>(sp => sp.GetRequiredService<SessionStore>());
        ServiceCollection.AddSingleton<ISessionCoordinator, SessionCoordinator>();
        ServiceCollection.AddSingleton<PairedDeviceStore>();
        ServiceCollection.AddSingleton<IPairedDeviceStore>(sp => sp.GetRequiredService<PairedDeviceStore>());
        ServiceCollection.AddSingleton<IPairedDeviceStoreWriter>(sp => sp.GetRequiredService<PairedDeviceStore>());
        ServiceCollection.AddSingleton<IPairedDeviceCoordinator, PairedDeviceCoordinator>();
        ServiceCollection.AddSingleton<ISyncCoordinator, SyncCoordinator>();
        ServiceCollection.AddSingleton<IImportSessionsCoordinator>(sp =>
            new ImportSessionsCoordinator(
                sp.GetRequiredService<IDatabaseService>(),
                sp.GetRequiredService<ISessionStoreWriter>(),
                sp.GetRequiredService<IShellCoordinator>(),
                () => sp.GetRequiredService<ImportSessionsViewModel>()));
        ServiceCollection.AddSingleton<BikeListViewModel>();
        ServiceCollection.AddSingleton<SessionListViewModel>();
        ServiceCollection.AddSingleton<PairedDeviceListViewModel>();
        ServiceCollection.AddSingleton<ImportSessionsViewModel>();
        ServiceCollection.AddSingleton<SetupListViewModel>();
        ServiceCollection.AddSingleton<MainPagesViewModel>(sp => new MainPagesViewModel(
            sp.GetRequiredService<IDatabaseService>(),
            sp.GetRequiredService<IBikeStoreWriter>(),
            sp.GetRequiredService<ISetupStoreWriter>(),
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<IPairedDeviceStoreWriter>(),
            sp.GetRequiredService<IImportSessionsCoordinator>(),
            sp.GetRequiredService<IFilesService>(),
            sp.GetRequiredService<ISyncCoordinator>(),
            sp.GetRequiredService<IShellCoordinator>(),
            sp.GetRequiredService<BikeListViewModel>(),
            sp.GetRequiredService<SessionListViewModel>(),
            sp.GetRequiredService<SetupListViewModel>(),
            sp.GetRequiredService<ImportSessionsViewModel>(),
            sp.GetRequiredService<PairedDeviceListViewModel>(),
            sp.GetService<PairingClientViewModel>(),
            sp.GetService<PairingServerViewModel>()));
        ServiceCollection.AddSingleton<WelcomeScreenViewModel>();
        ServiceCollection.AddSingleton<MainViewModel>();
        ServiceCollection.AddSingleton<MainWindowViewModel>();

        IsDesktop = ServiceCollection.Any(s => s.ServiceType == typeof(ISynchronizationServerService));
        Services = ServiceCollection.BuildServiceProvider();

        // Coordinators with constructor-time event subscriptions are
        // eagerly resolved here so the subscriptions are wired before any
        // sync, pairing, or telemetry arrival can happen.
        _ = Services.GetRequiredService<ISessionCoordinator>();
        _ = Services.GetRequiredService<IPairedDeviceCoordinator>();
        _ = Services.GetRequiredService<ISyncCoordinator>();

        // Mobile-only: eagerly resolve so DeviceId / IsPaired probe runs
        // before the pairing screen is opened.
        if (!IsDesktop)
        {
            _ = Services.GetService<IPairingClientCoordinator>();
        }

        // Desktop-only: eagerly resolve so the constructor's
        // PairingRequested/PairingConfirmed event subscriptions wire up
        // before the desktop view loads.
        if (IsDesktop)
        {
            _ = Services.GetService<IPairingServerCoordinator>();
            _ = Services.GetService<IInboundSyncCoordinator>();
        }

        var fileService = Services.GetRequiredService<IFilesService>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                fileService.SetTarget(TopLevel.GetTopLevel(desktop.MainWindow));
                dialogService.SetOwner(desktop.MainWindow);
                desktop.MainWindow.DataContext = mainWindowViewModel;
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = mainViewModel
                };
                singleViewPlatform.MainView.Loaded += (_, _) =>
                {
                    var topLevel = TopLevel.GetTopLevel(singleViewPlatform.MainView);
                    Debug.Assert(topLevel is not null); // TODO: use null-conditional assignment after switch to .net 10
                    topLevel.BackRequested += (_, e) =>
                    {
                        mainViewModel.OpenPreviousView();
                        e.Handled = true;
                    };
                    fileService.SetTarget(topLevel);
                };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}