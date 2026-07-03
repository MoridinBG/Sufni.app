using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Sufni.App.ExtensionHost.Runtime.Presentation;

public sealed class PlotZoomRequestedEventArgs : RoutedEventArgs
{
    public PlotZoomRequestedEventArgs(PlotZoomContainer container)
        : base(PlotZoomContainer.PlotZoomRequestedEvent)
    {
        Container = container;
    }

    public PlotZoomContainer Container { get; }
}

public class PlotZoomContainer : Border
{
    public static readonly RoutedEvent<PlotZoomRequestedEventArgs> PlotZoomRequestedEvent =
        RoutedEvent.Register<PlotZoomContainer, PlotZoomRequestedEventArgs>(
            nameof(PlotZoomRequested),
            RoutingStrategies.Bubble);

    private bool borrowedChildHadLocalDataContext;
    private object? borrowedChildLocalDataContext;

    public PlotZoomContainer() => AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);

    public event EventHandler<PlotZoomRequestedEventArgs>? PlotZoomRequested
    {
        add => AddHandler(PlotZoomRequestedEvent, value);
        remove => RemoveHandler(PlotZoomRequestedEvent, value);
    }

    public bool IsChildBorrowed { get; private set; }

    public Control? BorrowChild()
    {
        if (Child is not Control child || IsChildBorrowed)
        {
            return null;
        }

        borrowedChildHadLocalDataContext = child.IsSet(StyledElement.DataContextProperty);
        borrowedChildLocalDataContext = borrowedChildHadLocalDataContext ? child.DataContext : null;
        var effectiveDataContext = child.DataContext;

        Child = null;
        child.DataContext = effectiveDataContext;
        IsChildBorrowed = true;
        return child;
    }

    public void ReturnChild(Control child)
    {
        Child = child;

        if (borrowedChildHadLocalDataContext)
        {
            child.DataContext = borrowedChildLocalDataContext;
        }
        else
        {
            child.ClearValue(StyledElement.DataContextProperty);
        }

        borrowedChildHadLocalDataContext = false;
        borrowedChildLocalDataContext = null;
        IsChildBorrowed = false;
    }

    public static bool HasInteractiveControlOnRoute(object? source, Visual stopAt)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or ComboBox or TextBox or Slider or ScrollBar)
            {
                return true;
            }

            if (ReferenceEquals(current, stopAt))
            {
                break;
            }
        }

        return false;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsChildBorrowed || Child is null)
        {
            return;
        }

        if (HasInteractiveControlOnRoute(e.Source, this))
        {
            return;
        }

        e.Handled = true;
        RaiseEvent(new PlotZoomRequestedEventArgs(this));
    }
}
