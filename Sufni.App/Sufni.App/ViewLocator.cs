using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using Sufni.App.Extensibility.Views;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Bikes.ViewModels.Editors.Bike;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.Bikes.ViewModels.LinkageParts;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.LiveDaq.ViewModels.SessionPages;
using Sufni.App.Sessions.Analysis.ViewModels.SessionPages;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Graph.ViewModels.SessionPages;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Setups.ViewModels.SensorConfigurations;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.ViewModels;
using Sufni.App.SyncAndPairing.ViewModels.ItemLists;
using Sufni.App.Shared.Common;
namespace Sufni.App;

public class ViewLocator : IDataTemplate
{
    private readonly IExtensionViewRegistry extensionViewRegistry;
    private readonly IServiceProvider serviceProvider;

    private static readonly FrozenDictionary<Type, Func<Control>> ViewFactories = new Dictionary<Type, Func<Control>>
    {
        [typeof(MainViewModel)] = static () => new global::Sufni.App.Shell.Views.MainView(),
        [typeof(MainPagesViewModel)] = static () => new global::Sufni.App.Shell.Views.MainPagesView(),
        [typeof(WelcomeScreenViewModel)] = static () => new global::Sufni.App.Shell.Views.WelcomeScreenView(),
        [typeof(PairingClientViewModel)] = static () => new global::Sufni.App.SyncAndPairing.Views.PairingClientView(),
        [typeof(ImportSessionsViewModel)] = static () => new global::Sufni.App.Acquisition.Views.ImportSessionsView(),
        [typeof(BikeListViewModel)] = static () => new global::Sufni.App.Bikes.Views.ItemLists.BikeListView(),
        [typeof(LiveDaqListViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.ItemLists.LiveDaqListView(),
        [typeof(SessionListViewModel)] = static () => new global::Sufni.App.Sessions.Lists.Views.ItemLists.SessionListView(),
        [typeof(SetupListViewModel)] = static () => new global::Sufni.App.Setups.Views.ItemLists.SetupListView(),
        [typeof(BikeEditorViewModel)] = static () => new global::Sufni.App.Bikes.Views.Editors.BikeEditorView(),
        [typeof(LeverageRatioEditorViewModel)] = static () => new global::Sufni.App.Bikes.Views.Editors.LeverageRatioEditorView(),
        [typeof(LiveDaqConfigEditorViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.Editors.LiveDaqConfigEditorView(),
        [typeof(LiveDaqDetailViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.Editors.LiveDaqDetailView(),
        [typeof(LiveSessionDetailViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.Editors.LiveSessionDetailView(),
        [typeof(SessionDetailViewModel)] = static () => new global::Sufni.App.Sessions.Detail.Views.Editors.SessionDetailView(),
        [typeof(SetupEditorViewModel)] = static () => new global::Sufni.App.Setups.Views.Editors.SetupEditorView(),
        [typeof(JointViewModel)] = static () => new global::Sufni.App.Bikes.Views.LinkageParts.JointView(),
        [typeof(LinearForkSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.LinearForkSensorConfigurationView(),
        [typeof(LinearShockSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.LinearShockSensorConfigurationView(),
        [typeof(RotationalForkSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.RotationalForkSensorConfigurationView(),
        [typeof(RotationalShockSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.RotationalShockSensorConfigurationView(),
        [typeof(BalancePageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.BalancePageView(),
        [typeof(DamperPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.DamperPageView(),
        [typeof(LiveGraphPageViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.SessionPages.LiveGraphPageView(),
        [typeof(RecordedGraphPageViewModel)] = static () => new global::Sufni.App.Sessions.Graph.Views.SessionPages.RecordedGraphPageView(),
        [typeof(NotesPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.NotesPageView(),
        [typeof(PreferencesPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.PreferencesPageView(),
        [typeof(SessionAnalysisPageViewModel)] = static () => new global::Sufni.App.Sessions.Analysis.Views.SessionPages.SessionAnalysisPageView(),
        [typeof(SpringPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.SpringPageView(),
        [typeof(StrokesPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.StrokesPageView(),
        [typeof(VibrationPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.VibrationPageView(),
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, Func<Control>> DesktopViewFactories = new Dictionary<Type, Func<Control>>
    {
        [typeof(MainPagesViewModel)] = static () => new global::Sufni.App.Shell.DesktopViews.MainPagesDesktopView(),
        [typeof(ImportSessionsViewModel)] = static () => new global::Sufni.App.Acquisition.DesktopViews.ImportSessionsDesktopView(),
        [typeof(BikeListViewModel)] = static () => new global::Sufni.App.Bikes.DesktopViews.ItemLists.BikeListDesktopView(),
        [typeof(LiveDaqListViewModel)] = static () => new global::Sufni.App.LiveDaq.DesktopViews.ItemLists.LiveDaqListDesktopView(),
        [typeof(PairedDeviceListViewModel)] = static () => new global::Sufni.App.SyncAndPairing.DesktopViews.ItemLists.PairedDeviceListDesktopView(),
        [typeof(SessionListViewModel)] = static () => new global::Sufni.App.Sessions.Lists.DesktopViews.ItemLists.SessionListDesktopView(),
        [typeof(SetupListViewModel)] = static () => new global::Sufni.App.Setups.DesktopViews.ItemLists.SetupListDesktopView(),
        [typeof(BikeEditorViewModel)] = static () => new global::Sufni.App.Bikes.DesktopViews.Editors.BikeEditorDesktopView(),
        [typeof(LiveDaqDetailViewModel)] = static () => new global::Sufni.App.LiveDaq.DesktopViews.Editors.LiveDaqDetailDesktopView(),
        [typeof(LiveSessionDetailViewModel)] = static () => new global::Sufni.App.LiveDaq.DesktopViews.Editors.LiveSessionDetailDesktopView(),
        [typeof(SessionDetailViewModel)] = static () => new global::Sufni.App.Sessions.Detail.DesktopViews.Editors.SessionDetailDesktopView(),
        [typeof(SetupEditorViewModel)] = static () => new global::Sufni.App.Setups.DesktopViews.Editors.SetupEditorDesktopView(),
    }.ToFrozenDictionary();

    public ViewLocator()
        : this(new ExtensionViewRegistry(), EmptyServiceProvider.Instance)
    {
    }

    internal ViewLocator(IExtensionViewRegistry extensionViewRegistry)
        : this(extensionViewRegistry, EmptyServiceProvider.Instance)
    {
    }

    internal ViewLocator(IExtensionViewRegistry extensionViewRegistry, IServiceProvider serviceProvider)
    {
        this.extensionViewRegistry = extensionViewRegistry;
        this.serviceProvider = serviceProvider;
    }

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var isDesktop = App.Current?.IsDesktop == true;
        var viewModelType = data.GetType();

        if (data is RecordedSessionExtensionPageViewModel extensionPage)
        {
            return Build(extensionPage.ViewModel);
        }

        if (extensionViewRegistry.TryBuild(data, isDesktop, serviceProvider, out var extensionView))
        {
            return extensionView;
        }

        if (isDesktop && DesktopViewFactories.TryGetValue(viewModelType, out var desktopFactory))
        {
            return desktopFactory();
        }

        if (ViewFactories.TryGetValue(viewModelType, out var factory))
        {
            return factory();
        }

        var fallbackName = viewModelType.FullName!.Replace("ViewModel", isDesktop ? "DesktopView" : "View");
        return new TextBlock { Text = fallbackName };
    }

    public bool Match(object? data)
    {
        if (data is null) return false;

        var isDesktop = App.Current?.IsDesktop == true;
        var viewModelType = data.GetType();
        return data is ViewModelBase ||
               extensionViewRegistry.Matches(viewModelType, isDesktop) ||
               ViewFactories.ContainsKey(viewModelType) ||
               (isDesktop && DesktopViewFactories.ContainsKey(viewModelType));
    }

}
