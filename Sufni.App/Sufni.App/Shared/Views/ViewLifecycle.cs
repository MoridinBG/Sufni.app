using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sufni.App.Shared.Views;

/// <summary>
/// Runs bound commands from a control's lifecycle events. A command runs on every
/// occurrence of its event, and only when <see cref="ICommand.CanExecute"/> allows it,
/// so a view that is detached and attached again unloads and loads again.
/// </summary>
public static class ViewLifecycle
{
    public static readonly AttachedProperty<ICommand?> LoadedCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "LoadedCommand", typeof(ViewLifecycle));

    public static readonly AttachedProperty<object?> LoadedCommandParameterProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>(
            "LoadedCommandParameter", typeof(ViewLifecycle));

    public static readonly AttachedProperty<ICommand?> UnloadedCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "UnloadedCommand", typeof(ViewLifecycle));

    public static readonly AttachedProperty<ICommand?> AttachedToVisualTreeCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "AttachedToVisualTreeCommand", typeof(ViewLifecycle));

    public static readonly AttachedProperty<ICommand?> DetachedFromVisualTreeCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "DetachedFromVisualTreeCommand", typeof(ViewLifecycle));

    public static void SetLoadedCommand(Control element, ICommand? value) =>
        element.SetValue(LoadedCommandProperty, value);

    public static ICommand? GetLoadedCommand(Control element) =>
        element.GetValue(LoadedCommandProperty);

    public static void SetLoadedCommandParameter(Control element, object? value) =>
        element.SetValue(LoadedCommandParameterProperty, value);

    public static object? GetLoadedCommandParameter(Control element) =>
        element.GetValue(LoadedCommandParameterProperty);

    public static void SetUnloadedCommand(Control element, ICommand? value) =>
        element.SetValue(UnloadedCommandProperty, value);

    public static ICommand? GetUnloadedCommand(Control element) =>
        element.GetValue(UnloadedCommandProperty);

    public static void SetAttachedToVisualTreeCommand(Control element, ICommand? value) =>
        element.SetValue(AttachedToVisualTreeCommandProperty, value);

    public static ICommand? GetAttachedToVisualTreeCommand(Control element) =>
        element.GetValue(AttachedToVisualTreeCommandProperty);

    public static void SetDetachedFromVisualTreeCommand(Control element, ICommand? value) =>
        element.SetValue(DetachedFromVisualTreeCommandProperty, value);

    public static ICommand? GetDetachedFromVisualTreeCommand(Control element) =>
        element.GetValue(DetachedFromVisualTreeCommandProperty);

    static ViewLifecycle()
    {
        LoadedCommandProperty.Changed.AddClassHandler<Control>(OnLoadedCommandChanged);
        UnloadedCommandProperty.Changed.AddClassHandler<Control>(OnUnloadedCommandChanged);
        AttachedToVisualTreeCommandProperty.Changed.AddClassHandler<Control>(OnAttachedToVisualTreeCommandChanged);
        DetachedFromVisualTreeCommandProperty.Changed.AddClassHandler<Control>(OnDetachedFromVisualTreeCommandChanged);
    }

    private static void OnLoadedCommandChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        host.Loaded -= OnLoaded;
        if (e.NewValue is not null)
        {
            host.Loaded += OnLoaded;
        }
    }

    private static void OnUnloadedCommandChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        host.Unloaded -= OnUnloaded;
        if (e.NewValue is not null)
        {
            host.Unloaded += OnUnloaded;
        }
    }

    private static void OnAttachedToVisualTreeCommandChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        host.AttachedToVisualTree -= OnAttachedToVisualTree;
        if (e.NewValue is not null)
        {
            host.AttachedToVisualTree += OnAttachedToVisualTree;
        }
    }

    private static void OnDetachedFromVisualTreeCommandChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        host.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        if (e.NewValue is not null)
        {
            host.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control host)
        {
            Execute(GetLoadedCommand(host), GetLoadedCommandParameter(host));
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control host)
        {
            Execute(GetUnloadedCommand(host), null);
        }
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control host)
        {
            Execute(GetAttachedToVisualTreeCommand(host), null);
        }
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control host)
        {
            Execute(GetDetachedFromVisualTreeCommand(host), null);
        }
    }

    private static void Execute(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }
}
