using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sufni.App;

namespace Sufni.App.Tests.Infrastructure;

public static class ViewTestHelpers
{
    private const string ViewTemplatesRegisteredKey = "__ViewTestHelpers_ViewTemplatesRegistered";
    private const string ViewTemplatesDesktopModeKey = "__ViewTestHelpers_ViewTemplatesDesktopMode";

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

    public static async Task<Window> ShowViewAsync(Control view)
    {
        var host = ShowView(view);
        await FlushDispatcherAsync();

        var width = host.Width > 0 ? host.Width : 900;
        var height = host.Height > 0 ? host.Height : 700;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        await FlushDispatcherAsync();

        return host;
    }

    public static async Task FlushDispatcherAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    public static void EnsureViewTestResources()
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        resources["SufniForeground"] = Color.Parse("#a0a0a0");
        resources["SufniForegroundPointerOver"] = Color.Parse("#c0c0c0");
        resources["SufniRegion"] = Color.Parse("#15191c");
        resources["SufniBackground"] = Color.Parse("#20262b");
        resources["SufniForegroundDisabled"] = Color.Parse("#606060");
        resources["SufniBackgroundDisabled"] = Color.Parse("#25292c");
        resources["SufniBackgroundPointerOver"] = Color.Parse("#2c3032");
        resources["SufniItemBackgroundPointerOver"] = Color.Parse("#1f2327");
        resources["SufniBorderBrush"] = Color.Parse("#505050");
        resources["SufniAccentColor"] = Color.Parse("#0078d7");
        resources["SufniDangerColor"] = Color.Parse("#bf312d");
        resources["SufniDangerColorDark"] = Color.Parse("#9f110d");
        resources["SufniGridSplitter"] = Color.Parse("#404040");
    }

    public static void EnsureViewTestDataTemplates(bool isDesktop)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        TestApp.SetIsDesktop(isDesktop);

        if (application.Resources.TryGetValue(ViewTemplatesRegisteredKey, out var registered)
            && registered is true
            && application.Resources.TryGetValue(ViewTemplatesDesktopModeKey, out var currentMode)
            && currentMode is bool registeredDesktopMode
            && registeredDesktopMode == isDesktop)
        {
            return;
        }

        foreach (var existingLocator in application.DataTemplates.OfType<ViewLocator>().ToArray())
        {
            application.DataTemplates.Remove(existingLocator);
        }

        application.DataTemplates.Add(new ViewLocator());
        application.Resources[ViewTemplatesRegisteredKey] = true;
        application.Resources[ViewTemplatesDesktopModeKey] = isDesktop;
    }

    public static void EnsurePlotViewStyle()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var source = new Uri("avares://Sufni.App/Views/Plots/SufniPlotView.axaml");

        if (application.Styles.OfType<StyleInclude>().Any(style => style.Source?.AbsoluteUri == source.AbsoluteUri))
        {
            return;
        }

        application.Styles.Add(new StyleInclude(new Uri("avares://Sufni.App/"))
        {
            Source = source
        });
    }

    public static void EnsureSessionDetailViewSetup(bool isDesktop)
    {
        EnsureViewTestResources();
        EnsureViewTestDataTemplates(isDesktop);

        if (isDesktop)
        {
            EnsurePlotViewStyle();
        }
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
