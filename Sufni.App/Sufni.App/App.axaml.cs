using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Services;
using Sufni.App.ViewModels;
using Sufni.App.Views;
using System;
using System.Diagnostics;
using Avalonia.Controls;

namespace Sufni.App;

public partial class App : Application
{
    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }

    public App()
    {
#if DEBUG
        RegisteredServices.Collection.AddSingleton<IHttpApiService, HttpApiServiceStub>();
#else
        RegisteredServices.Collection.AddSingleton<IHttpApiService, HttpApiService>();
#endif
        RegisteredServices.Collection.AddSingleton<ITelemetryDataStoreService, TelemetryDataStoreService>();
        RegisteredServices.Collection.AddSingleton<IDatabaseService, SqLiteDatabaseService>();
        RegisteredServices.Collection.AddSingleton<ISynchronizationService, SynchronizationService>();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    public override void OnFrameworkInitializationCompleted()
    {
        RegisteredServices.Collection.AddSingleton<IFilesService>(_ => new FilesService());
        RegisteredServices.Collection.AddSingleton<MainPagesViewModel>();
        RegisteredServices.Collection.AddSingleton<MainViewModel>();
        Services = RegisteredServices.Collection.BuildServiceProvider();
        
        var fileService = Services.GetService<IFilesService>();
        var mainViewModel = Services.GetService<MainViewModel>();
        Debug.Assert(fileService != null, nameof(fileService) + " != null");

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                fileService.SetTarget(TopLevel.GetTopLevel(desktop.MainWindow));
                desktop.MainWindow.DataContext = mainViewModel;
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = mainViewModel
                };
                singleViewPlatform.MainView.Loaded += (_, _) =>
                {
                    var topLevel = TopLevel.GetTopLevel(singleViewPlatform.MainView);
                    mainViewModel!.SafeAreaPadding = topLevel!.InsetsManager!.SafeAreaPadding;
                    fileService.SetTarget(topLevel);
                };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}