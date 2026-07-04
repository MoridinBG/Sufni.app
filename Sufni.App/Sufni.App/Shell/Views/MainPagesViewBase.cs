using Avalonia.Controls;
using Avalonia;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.Views;

public partial class MainPagesViewBase : UserControl
{
    private readonly List<IDisposable> subscriptions = [];

    public static readonly StyledProperty<MainPagesViewModel?> MainPagesProperty =
        AvaloniaProperty.Register<MainPagesViewBase, MainPagesViewModel?>(nameof(MainPages));

    public MainPagesViewModel? MainPages
    {
        get => GetValue(MainPagesProperty);
        set => SetValue(MainPagesProperty, value);
    }

    protected static void RegisterDrawerMenuAutoClose(Control menuPanel, DrawerPage drawerPage)
    {
        menuPanel.Loaded += (_, _) =>
        {
            var menuItems = menuPanel.GetVisualDescendants().OfType<MenuItem>();
            foreach (var menuItem in menuItems)
            {
                menuItem.PointerPressed += (_, _) =>
                {
                    drawerPage.IsOpen = false;
                };
            }
        };
    }

    protected void RegisterPrimaryPageSelection(TabbedPage tabbedPage)
    {
        MainPagesViewModel? selectedMainPages = null;
        var syncingSelection = false;

        void SetTabbedPageSelectedIndex(int selectedIndex)
        {
            if (tabbedPage.SelectedIndex == selectedIndex)
            {
                return;
            }

            syncingSelection = true;
            try
            {
                tabbedPage.SelectedIndex = selectedIndex;
            }
            finally
            {
                syncingSelection = false;
            }
        }

        void OnMainPagesPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainPagesViewModel.SelectedPrimaryIndex) &&
                selectedMainPages is not null)
            {
                SetTabbedPageSelectedIndex(selectedMainPages.SelectedPrimaryIndex);
            }
        }

        void AttachMainPages(MainPagesViewModel? pages)
        {
            if (ReferenceEquals(selectedMainPages, pages))
            {
                return;
            }

            if (selectedMainPages is not null)
            {
                selectedMainPages.PropertyChanged -= OnMainPagesPropertyChanged;
            }

            selectedMainPages = pages;
            if (selectedMainPages is null)
            {
                return;
            }

            selectedMainPages.PropertyChanged += OnMainPagesPropertyChanged;
            SetTabbedPageSelectedIndex(selectedMainPages.SelectedPrimaryIndex);
        }

        tabbedPage.PropertyChanged += (_, args) =>
        {
            if (args.Property != TabbedPage.SelectedIndexProperty ||
                syncingSelection ||
                selectedMainPages is null ||
                tabbedPage.SelectedIndex < 0)
            {
                return;
            }

            selectedMainPages.SelectedPrimaryIndex = tabbedPage.SelectedIndex;
        };

        subscriptions.Add(this.GetObservable(MainPagesProperty).Subscribe(AttachMainPages));
    }
}
