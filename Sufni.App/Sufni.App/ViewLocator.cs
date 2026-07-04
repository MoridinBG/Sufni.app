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
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.SessionPages;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Setups.ViewModels.SensorConfigurations;
using Sufni.App.Infrastructure;
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
    private readonly IAppEnvironment? appEnvironment;

    private static readonly FrozenDictionary<Type, Func<Control>> CommonViewFactories = new Dictionary<Type, Func<Control>>
    {
        [typeof(WelcomeScreenViewModel)] = static () => new global::Sufni.App.Shell.Views.WelcomeScreenView(),
        [typeof(PairingClientViewModel)] = static () => new global::Sufni.App.SyncAndPairing.Views.PairingClientView(),
        [typeof(LeverageRatioEditorViewModel)] = static () => new global::Sufni.App.Bikes.Views.Editors.LeverageRatioEditorView(),
        [typeof(LiveDaqConfigEditorViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.Editors.LiveDaqConfigEditorView(),
        [typeof(JointViewModel)] = static () => new global::Sufni.App.Bikes.Views.LinkageParts.JointView(),
        [typeof(LinearForkSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.LinearForkSensorConfigurationView(),
        [typeof(LinearShockSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.LinearShockSensorConfigurationView(),
        [typeof(RotationalForkSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.RotationalForkSensorConfigurationView(),
        [typeof(RotationalShockSensorConfigurationViewModel)] = static () => new global::Sufni.App.Setups.Views.SensorConfigurations.RotationalShockSensorConfigurationView(),
        [typeof(BalancePageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.BalancePageView(),
        [typeof(DampingPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.DampingPageView(),
        [typeof(LiveSignalsPageViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.SessionPages.LiveSignalsPageView(),
        [typeof(RecordedSignalsPageViewModel)] = static () => new global::Sufni.App.Sessions.Signals.Views.SessionPages.RecordedSignalsPageView(),
        [typeof(NotesPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.NotesPageView(),
        [typeof(PreferencesPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.PreferencesPageView(),
        [typeof(SessionInsightsPageViewModel)] = static () => new global::Sufni.App.Sessions.Insights.Views.SessionPages.SessionInsightsPageView(),
        [typeof(SpringPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.SpringPageView(),
        [typeof(StrokesPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.StrokesPageView(),
        [typeof(VibrationPageViewModel)] = static () => new global::Sufni.App.Sessions.Pages.Views.SessionPages.VibrationPageView(),
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, Func<Control>> CompactViewFactories = new Dictionary<Type, Func<Control>>
    {
        [typeof(MainPagesViewModel)] = static () => new global::Sufni.App.Shell.Views.MainPagesView(),
        [typeof(ImportSessionsViewModel)] = static () => new global::Sufni.App.Acquisition.Views.ImportSessionsView(),
        [typeof(BikeListViewModel)] = static () => new global::Sufni.App.Bikes.Views.ItemLists.BikeListView(),
        [typeof(LiveDaqListViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.ItemLists.LiveDaqListView(),
        [typeof(PairedDeviceListViewModel)] = static () => new global::Sufni.App.SyncAndPairing.DesktopViews.ItemLists.PairedDeviceListDesktopView(),
        [typeof(SessionListViewModel)] = static () => new global::Sufni.App.Sessions.Lists.Views.ItemLists.SessionListView(),
        [typeof(SetupListViewModel)] = static () => new global::Sufni.App.Setups.Views.ItemLists.SetupListView(),
        [typeof(BikeEditorViewModel)] = static () => new global::Sufni.App.Bikes.Views.Editors.BikeEditorView(),
        [typeof(LiveDaqDetailViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.Editors.LiveDaqDetailView(),
        [typeof(LiveSessionDetailViewModel)] = static () => new global::Sufni.App.LiveDaq.Views.Editors.LiveSessionDetailView(),
        [typeof(SessionDetailViewModel)] = static () => new global::Sufni.App.Sessions.Detail.Views.Editors.SessionDetailView(),
        [typeof(SetupEditorViewModel)] = static () => new global::Sufni.App.Setups.Views.Editors.SetupEditorView(),
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, Func<Control>> WorkspaceViewFactories = new Dictionary<Type, Func<Control>>
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
        : this(new ExtensionViewRegistry(), EmptyServiceProvider.Instance, appEnvironment: null)
    {
    }

    internal ViewLocator(IExtensionViewRegistry extensionViewRegistry)
        : this(extensionViewRegistry, EmptyServiceProvider.Instance, appEnvironment: null)
    {
    }

    internal ViewLocator(IAppEnvironment appEnvironment)
        : this(new ExtensionViewRegistry(), EmptyServiceProvider.Instance, appEnvironment)
    {
    }

    internal ViewLocator(IExtensionViewRegistry extensionViewRegistry, IServiceProvider serviceProvider)
        : this(
            extensionViewRegistry,
            serviceProvider,
            serviceProvider.GetService(typeof(IAppEnvironment)) as IAppEnvironment)
    {
    }

    private ViewLocator(
        IExtensionViewRegistry extensionViewRegistry,
        IServiceProvider serviceProvider,
        IAppEnvironment? appEnvironment)
    {
        this.extensionViewRegistry = extensionViewRegistry;
        this.serviceProvider = serviceProvider;
        this.appEnvironment = appEnvironment;
    }

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var viewModelType = data.GetType();

        if (data is RecordedSessionExtensionPageViewModel extensionPage)
        {
            return Build(extensionPage.ViewModel);
        }

        if (data is ShellRootViewModel { LayoutProfile: UiLayoutProfile.Compact })
        {
            return new global::Sufni.App.Shell.Views.CompactShellView();
        }

        if (data is ShellRootViewModel { LayoutProfile: UiLayoutProfile.Workspace })
        {
            return new global::Sufni.App.Shell.DesktopViews.WorkspaceShellView();
        }

        var layoutProfile = ResolveLayoutProfile();
        if (extensionViewRegistry.TryBuild(data, layoutProfile, serviceProvider, out var extensionView))
        {
            return extensionView;
        }

        if (TryBuildProfileView(viewModelType, layoutProfile, out var profileView))
        {
            return profileView;
        }

        var fallbackName = viewModelType.FullName!.Replace("ViewModel", FallbackViewSuffix(layoutProfile));
        return new TextBlock { Text = fallbackName };
    }

    public bool Match(object? data)
    {
        if (data is null) return false;

        if (data is RecordedSessionExtensionPageViewModel extensionPage)
        {
            return Match(extensionPage.ViewModel);
        }

        var layoutProfile = ResolveLayoutProfile();
        var viewModelType = data.GetType();
        return data is ViewModelBase ||
               extensionViewRegistry.Matches(viewModelType, layoutProfile) ||
               CommonViewFactories.ContainsKey(viewModelType) ||
               CompactViewFactories.ContainsKey(viewModelType) ||
               WorkspaceViewFactories.ContainsKey(viewModelType);
    }

    private UiLayoutProfile ResolveLayoutProfile() =>
        appEnvironment?.LayoutProfile ?? UiLayoutProfile.Compact;

    private static bool TryBuildProfileView(
        Type viewModelType,
        UiLayoutProfile layoutProfile,
        out Control control)
    {
        var profileFactories = layoutProfile == UiLayoutProfile.Workspace
            ? WorkspaceViewFactories
            : CompactViewFactories;

        if (profileFactories.TryGetValue(viewModelType, out var profileFactory))
        {
            control = profileFactory();
            return true;
        }

        if (CommonViewFactories.TryGetValue(viewModelType, out var commonFactory))
        {
            control = commonFactory();
            return true;
        }

        control = null!;
        return false;
    }

    private static string FallbackViewSuffix(UiLayoutProfile layoutProfile) =>
        layoutProfile == UiLayoutProfile.Workspace ? "DesktopView" : "View";
}
