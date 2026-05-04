using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.Views.Editors;

public partial class SessionShellMobileView : UserControl
{
    public static readonly StyledProperty<ICommand?> LoadedCommandProperty =
        AvaloniaProperty.Register<SessionShellMobileView, ICommand?>(nameof(LoadedCommand));

    public static readonly StyledProperty<ICommand?> UnloadedCommandProperty =
        AvaloniaProperty.Register<SessionShellMobileView, ICommand?>(nameof(UnloadedCommand));

    public static readonly StyledProperty<Control?> ControlContentProperty =
        AvaloniaProperty.Register<SessionShellMobileView, Control?>(nameof(ControlContent));

    public ICommand? LoadedCommand
    {
        get => GetValue(LoadedCommandProperty);
        set => SetValue(LoadedCommandProperty, value);
    }

    public ICommand? UnloadedCommand
    {
        get => GetValue(UnloadedCommandProperty);
        set => SetValue(UnloadedCommandProperty, value);
    }

    public Control? ControlContent
    {
        get => GetValue(ControlContentProperty);
        set => SetValue(ControlContentProperty, value);
    }

    private bool sizeChanging;
    private bool dropDownOpen;
    private Vector lockedOffset;
    private readonly List<IDisposable> dropDownSubscriptions = new();
    private readonly HashSet<ComboBox> trackedComboBoxes = new();

    public SessionShellMobileView()
    {
        InitializeComponent();
        TabContainer.Loaded += (_, _) => SubscribeToComboBoxes();
        DetachedFromVisualTree += (_, _) => UnsubscribeFromComboBoxes();
        TabHeaders.Items.CollectionChanged += (_, _) =>
        {
            if (TabHeaders.ItemCount == 0)
            {
                return;
            }

            var selectedIndex = -1;
            for (var i = 0; i < TabHeaders.ItemCount; i++)
            {
                if ((TabHeaders.Items[i] as PageViewModelBase)?.Selected == true)
                {
                    if (selectedIndex < 0)
                    {
                        selectedIndex = i;
                    }
                    else
                    {
                        (TabHeaders.Items[i] as PageViewModelBase)!.Selected = false;
                    }
                }
            }

            if (selectedIndex < 0)
            {
                selectedIndex = 0;
                (TabHeaders.Items[0] as PageViewModelBase)!.Selected = true;
            }

            var width = TabScrollViewer.Viewport.Width;
            if (width > 0)
            {
                sizeChanging = true;
                TabScrollViewer.Offset = new Vector(selectedIndex * width, 0);
                sizeChanging = false;
            }
        };
    }

    private void OnTabHeaderClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button) { return; }

        sizeChanging = true;

        var index = 0;
        for (var i = 0; i < TabHeaders.ItemCount; ++i)
        {
            var page = (TabHeaders.Items[i] as PageViewModelBase);
            if (page != null)
            {
                if (page.DisplayName == button.Name)
                {
                    index = i;
                }
                page.Selected = false;
            }
        }

        button.IsEnabled = false;

        var w = TabScrollViewer.Viewport.Width;
        TabScrollViewer.Offset = new Vector(index * w, 0);

        sizeChanging = false;
    }

    private void TabScrollViewer_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name != nameof(TabScrollViewer.Offset) || sizeChanging) return;

        if (dropDownOpen)
        {
            RestoreLockedOffset();
            return;
        }

        foreach (var header in TabHeaders.Items)
        {
            (header as PageViewModelBase)!.Selected = false;
        }

        var width = TabScrollViewer.Viewport.Width;
        var offset = TabScrollViewer.Offset.X;
        var index = (int)(offset + width / 2.0) / (int)width;
        (TabHeaders.Items[index] as PageViewModelBase)!.Selected = true;
    }

    private void TabScrollViewer_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        sizeChanging = true;

        for (var i = 0; i < TabHeaders.ItemCount; i++)
        {
            if ((TabHeaders.Items[i] as PageViewModelBase)!.Selected)
            {
                TabScrollViewer.Offset = new Vector(i * e.NewSize.Width, 0);
                break;
            }
        }

        sizeChanging = false;
    }

    private void SubscribeToComboBoxes()
    {
        foreach (var combo in TabContainer.GetVisualDescendants().OfType<ComboBox>())
        {
            if (!trackedComboBoxes.Add(combo))
            {
                continue;
            }

            var subscription = combo.GetObservable(ComboBox.IsDropDownOpenProperty).Subscribe(isOpen =>
            {
                if (isOpen)
                {
                    dropDownOpen = true;
                    lockedOffset = GetSelectedPageOffset();
                    RestoreLockedOffset();
                }
                else
                {
                    dropDownOpen = false;
                }
            });
            dropDownSubscriptions.Add(subscription);
        }
    }

    private void UnsubscribeFromComboBoxes()
    {
        foreach (var subscription in dropDownSubscriptions)
        {
            subscription.Dispose();
        }
        dropDownSubscriptions.Clear();
        trackedComboBoxes.Clear();
        dropDownOpen = false;
    }

    private Vector GetSelectedPageOffset()
    {
        var width = TabScrollViewer.Viewport.Width;
        if (width <= 0)
        {
            width = TabScrollViewer.Bounds.Width;
        }

        for (var i = 0; i < TabHeaders.ItemCount; i++)
        {
            if ((TabHeaders.Items[i] as PageViewModelBase)?.Selected == true)
            {
                return new Vector(i * width, 0);
            }
        }

        return TabScrollViewer.Offset;
    }

    private void RestoreLockedOffset()
    {
        if (TabScrollViewer.Offset == lockedOffset)
        {
            return;
        }

        sizeChanging = true;
        TabScrollViewer.Offset = lockedOffset;
        sizeChanging = false;
    }
}
