using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Services;
using Sufni.App.ViewModels;
using Sufni.App.Views;
using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;

namespace Sufni.App;

public partial class App : Application
{
    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }
    public bool IsDesktop { get; private set; }

    public App()
    {
        RegisteredServices.Collection.AddSingleton<IHttpApiService, HttpApiService>();
        RegisteredServices.Collection.AddSingleton<ITelemetryDataStoreService, TelemetryDataStoreService>();
        RegisteredServices.Collection.AddSingleton<IDatabaseService, SqLiteDatabaseService>();

        IsDesktop = RegisteredServices.Collection.Any(s => s.ServiceType == typeof(ISynchronizationServerService));
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    public override void OnFrameworkInitializationCompleted()
    {
        RegisteredServices.Collection.AddSingleton<IFilesService>(_ => new FilesService());
        RegisteredServices.Collection.AddSingleton<IDialogService>(_ => new DialogService());
        RegisteredServices.Collection.AddSingleton<MainPagesViewModel>();
        RegisteredServices.Collection.AddSingleton<MainViewModel>();
        RegisteredServices.Collection.AddSingleton<MainWindowViewModel>();
        Services = RegisteredServices.Collection.BuildServiceProvider();

        var fileService = Services.GetService<IFilesService>();
        var dialogService = Services.GetService<IDialogService>();
        var mainViewModel = Services.GetService<MainViewModel>();
        var mainWindowViewModel = Services.GetService<MainWindowViewModel>();
        Debug.Assert(fileService is not null);
        Debug.Assert(dialogService is not null);

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                fileService.SetTarget(TopLevel.GetTopLevel(desktop.MainWindow));
                dialogService.SetOwner(desktop.MainWindow);
                desktop.MainWindow.DataContext = mainWindowViewModel;
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                Debug.Assert(mainViewModel is not null);

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