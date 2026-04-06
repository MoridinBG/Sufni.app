using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Services;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Factories;
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
            ServiceCollection.AddSingleton<INavigator>(sp =>
                new DesktopNavigator(() => sp.GetRequiredService<MainWindowViewModel>()));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            ServiceCollection.AddSingleton<INavigator>(sp =>
                new MobileNavigator(() => sp.GetRequiredService<MainViewModel>()));
        }

        ServiceCollection.AddSingleton<IHttpApiService, HttpApiService>();
        ServiceCollection.AddSingleton<ITelemetryDataStoreService, TelemetryDataStoreService>();
        ServiceCollection.AddSingleton<IDatabaseService, SqLiteDatabaseService>();
        ServiceCollection.AddSingleton<IFilesService>(_ => new FilesService());
        ServiceCollection.AddSingleton<IDialogService>(_ => new DialogService());
        ServiceCollection.AddSingleton<IBikeViewModelFactory, BikeViewModelFactory>();
        ServiceCollection.AddSingleton<ISetupViewModelFactory, SetupViewModelFactory>();
        ServiceCollection.AddSingleton<ISessionViewModelFactory, SessionViewModelFactory>();
        ServiceCollection.AddSingleton<Func<SetupListViewModel>>(sp => () => sp.GetRequiredService<SetupListViewModel>());
        ServiceCollection.AddSingleton<Func<ISetupCreator>>(sp => () => sp.GetRequiredService<ISetupCreator>());
        ServiceCollection.AddSingleton<IBikeUsageQuery, BikeUsageQuery>();
        ServiceCollection.AddSingleton<BikeListViewModel>();
        ServiceCollection.AddSingleton<IBikeSelectionSource>(sp => sp.GetRequiredService<BikeListViewModel>());
        ServiceCollection.AddSingleton<IBikeCreator>(sp => sp.GetRequiredService<BikeListViewModel>());
        ServiceCollection.AddSingleton<SessionListViewModel>();
        ServiceCollection.AddSingleton<ISessionSink>(sp => sp.GetRequiredService<SessionListViewModel>());
        ServiceCollection.AddSingleton<PairedDeviceListViewModel>();
        ServiceCollection.AddSingleton<ImportSessionsViewModel>();
        ServiceCollection.AddSingleton<IImportSessionsOpener>(sp => sp.GetRequiredService<ImportSessionsViewModel>());
        ServiceCollection.AddSingleton<SetupListViewModel>();
        ServiceCollection.AddSingleton<ISetupCreator>(sp => sp.GetRequiredService<SetupListViewModel>());
        ServiceCollection.AddSingleton<MainPagesViewModel>(sp => new MainPagesViewModel(
            sp.GetRequiredService<IDatabaseService>(),
            sp.GetRequiredService<IBikeViewModelFactory>(),
            sp.GetRequiredService<ISetupViewModelFactory>(),
            sp.GetRequiredService<ISessionViewModelFactory>(),
            sp.GetRequiredService<IFilesService>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<BikeListViewModel>(),
            sp.GetRequiredService<SessionListViewModel>(),
            sp.GetRequiredService<SetupListViewModel>(),
            sp.GetRequiredService<ImportSessionsViewModel>(),
            sp.GetRequiredService<PairedDeviceListViewModel>(),
            sp.GetService<ISynchronizationServerService>(),
            sp.GetService<ISynchronizationClientService>(),
            sp.GetService<PairingClientViewModel>(),
            sp.GetService<PairingServerViewModel>()));
        ServiceCollection.AddSingleton<WelcomeScreenViewModel>();
        ServiceCollection.AddSingleton<MainViewModel>();
        ServiceCollection.AddSingleton<MainWindowViewModel>();

        IsDesktop = ServiceCollection.Any(s => s.ServiceType == typeof(ISynchronizationServerService));
        Services = ServiceCollection.BuildServiceProvider();

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