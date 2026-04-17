using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.App.ViewModels.SensorConfigurations;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App;

public class ViewLocator : IDataTemplate
{
    private static readonly IReadOnlyDictionary<Type, Func<Control>> ViewFactories = new Dictionary<Type, Func<Control>>
    {
        [typeof(MainViewModel)] = static () => new global::Sufni.App.Views.MainView(),
        [typeof(MainPagesViewModel)] = static () => new global::Sufni.App.Views.MainPagesView(),
        [typeof(WelcomeScreenViewModel)] = static () => new global::Sufni.App.Views.WelcomeScreenView(),
        [typeof(PairingClientViewModel)] = static () => new global::Sufni.App.Views.PairingClientView(),
        [typeof(ImportSessionsViewModel)] = static () => new global::Sufni.App.Views.ImportSessionsView(),
        [typeof(BikeListViewModel)] = static () => new global::Sufni.App.Views.ItemLists.BikeListView(),
        [typeof(SessionListViewModel)] = static () => new global::Sufni.App.Views.ItemLists.SessionListView(),
        [typeof(SetupListViewModel)] = static () => new global::Sufni.App.Views.ItemLists.SetupListView(),
        [typeof(BikeEditorViewModel)] = static () => new global::Sufni.App.Views.Editors.BikeEditorView(),
        [typeof(SessionDetailViewModel)] = static () => new global::Sufni.App.Views.Editors.SessionDetailView(),
        [typeof(SetupEditorViewModel)] = static () => new global::Sufni.App.Views.Editors.SetupEditorView(),
        [typeof(JointViewModel)] = static () => new global::Sufni.App.Views.LinkageParts.JointView(),
        [typeof(LinearForkSensorConfigurationViewModel)] = static () => new global::Sufni.App.Views.SensorConfigurations.LinearForkSensorConfigurationView(),
        [typeof(LinearShockSensorConfigurationViewModel)] = static () => new global::Sufni.App.Views.SensorConfigurations.LinearShockSensorConfigurationView(),
        [typeof(RotationalForkSensorConfigurationViewModel)] = static () => new global::Sufni.App.Views.SensorConfigurations.RotationalForkSensorConfigurationView(),
        [typeof(RotationalShockSensorConfigurationViewModel)] = static () => new global::Sufni.App.Views.SensorConfigurations.RotationalShockSensorConfigurationView(),
        [typeof(BalancePageViewModel)] = static () => new global::Sufni.App.Views.SessionPages.BalancePageView(),
        [typeof(DamperPageViewModel)] = static () => new global::Sufni.App.Views.SessionPages.DamperPageView(),
        [typeof(NotesPageViewModel)] = static () => new global::Sufni.App.Views.SessionPages.NotesPageView(),
        [typeof(SpringPageViewModel)] = static () => new global::Sufni.App.Views.SessionPages.SpringPageView(),
    };

    private static readonly IReadOnlyDictionary<Type, Func<Control>> DesktopViewFactories = new Dictionary<Type, Func<Control>>
    {
        [typeof(MainPagesViewModel)] = static () => new global::Sufni.App.DesktopViews.MainPagesDesktopView(),
        [typeof(ImportSessionsViewModel)] = static () => new global::Sufni.App.DesktopViews.ImportSessionsDesktopView(),
        [typeof(BikeListViewModel)] = static () => new global::Sufni.App.DesktopViews.ItemLists.BikeListDesktopView(),
        [typeof(LiveDaqListViewModel)] = static () => new global::Sufni.App.DesktopViews.ItemLists.LiveDaqListDesktopView(),
        [typeof(PairedDeviceListViewModel)] = static () => new global::Sufni.App.DesktopViews.ItemLists.PairedDeviceListDesktopView(),
        [typeof(SessionListViewModel)] = static () => new global::Sufni.App.DesktopViews.ItemLists.SessionListDesktopView(),
        [typeof(SetupListViewModel)] = static () => new global::Sufni.App.DesktopViews.ItemLists.SetupListDesktopView(),
        [typeof(BikeEditorViewModel)] = static () => new global::Sufni.App.DesktopViews.Editors.BikeEditorDesktopView(),
        [typeof(LiveDaqDetailViewModel)] = static () => new global::Sufni.App.DesktopViews.Editors.LiveDaqDetailDesktopView(),
        [typeof(SessionDetailViewModel)] = static () => new global::Sufni.App.DesktopViews.Editors.SessionDetailDesktopView(),
        [typeof(SetupEditorViewModel)] = static () => new global::Sufni.App.DesktopViews.Editors.SetupEditorDesktopView(),
    };

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        Debug.Assert(App.Current is not null);
        var isDesktop = App.Current.IsDesktop;
        var viewModelType = data.GetType();

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
        return data is ViewModelBase;
    }
}