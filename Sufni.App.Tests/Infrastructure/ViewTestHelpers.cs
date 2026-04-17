using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sufni.App;

namespace Sufni.App.Tests.Infrastructure;

public static class ViewTestHelpers
{
    private const string ViewTemplatesRegisteredKey = "__ViewTestHelpers_ViewTemplatesRegistered";

    public static Window ShowView(Control view)
    {
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        window.Show();
        return window;
    }

    public static Window ShowView<TView>(object dataContext)
        where TView : Control, new()
    {
        return ShowView(new TView { DataContext = dataContext });
    }

    public static async Task FlushDispatcherAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    public static void EnsureViewTestResources()
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        resources["SufniRegion"] = Brushes.Gray;
        resources["SufniForeground"] = Brushes.Gainsboro;
        resources["SufniForegroundPointerOver"] = Brushes.White;
        resources["SufniBackground"] = Brushes.DimGray;
        resources["SufniForegroundDisabled"] = Brushes.DarkGray;
        resources["SufniBackgroundDisabled"] = Brushes.Gray;
        resources["SufniBackgroundPointerOver"] = Brushes.SlateGray;
        resources["SufniItemBackgroundPointerOver"] = Brushes.SlateGray;
        resources["SufniBorderBrush"] = Brushes.Black;
        resources["SufniAccentColor"] = Brushes.CornflowerBlue;
        resources["SufniDangerColor"] = Brushes.Red;
        resources["SufniDangerColorDark"] = Brushes.DarkRed;
    }

    public static void EnsureViewTestDataTemplates()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        if (application.Resources.ContainsKey(ViewTemplatesRegisteredKey))
        {
            return;
        }

        application.DataTemplates.Add(new ViewLocator());
        application.Resources[ViewTemplatesRegisteredKey] = true;
    }

    public static T? FindFirstVisual<T>(this Control root)
        where T : Visual
    {
        return root.GetVisualDescendants().OfType<T>().FirstOrDefault();
    }

    public static T[] FindAllVisual<T>(this Control root)
        where T : Visual
    {
        return root.GetVisualDescendants().OfType<T>().ToArray();
    }
}
