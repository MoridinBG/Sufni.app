using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.Views.Editors;

public partial class SessionDetailView : UserControl
{
    private bool sizeChanging;
    public SessionDetailView()
    {
        InitializeComponent();
        TabHeaders.Items.CollectionChanged += (_, _) =>
        {
            if (TabHeaders.ItemCount > 0)
            {
                (TabHeaders.Items[0] as PageViewModelBase)!.Selected = true;
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
