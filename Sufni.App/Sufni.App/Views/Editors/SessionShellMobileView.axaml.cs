using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    public SessionShellMobileView()
    {
        InitializeComponent();
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
}
