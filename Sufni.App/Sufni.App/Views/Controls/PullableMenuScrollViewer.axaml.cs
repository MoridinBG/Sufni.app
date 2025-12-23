using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Labs.Controls.Base.Pan;
using Avalonia.Media;
using Avalonia.Threading;
using HapticFeedback;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.ViewModels;

namespace Sufni.App.Views.Controls;

public class MenuItemSelectedConverter : IValueConverter
{
    public static readonly MenuItemSelectedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool selected)
        {
            return selected ? FontWeight.DemiBold : FontWeight.Regular;
        }

        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public partial class PullableMenuScrollViewer : UserControl
{
    private static readonly double pullRatio = 3;
    private static readonly double menuItemHeight = 30;
    private readonly DoubleTransition heightTransition = new()
    {
        Duration = TimeSpan.FromSeconds(0.2),
        Property = HeightProperty,
        Easing = new CubicEaseOut(),
    };
    private readonly ThicknessTransition marginTransition = new()
    {
        Duration = TimeSpan.FromSeconds(0.2),
        Property = MarginProperty,
        Easing = new CubicEaseOut(),
    };
    private double totalPulled;
    private double sameDirectionTotalScrolled;
    private int? selectedIndex;
    private readonly IHapticFeedback? hapticFeedback = App.Current?.Services?.GetService<IHapticFeedback>();
    public PullableMenuScrollViewer()
    {
        InitializeComponent();
        Scroll.Transitions = [];
        Container.Transitions = [];
        TopContainer.Transitions = [];

        TopContainer.SizeChanged += (s, e) =>
        {
            var h = Bounds.Height - TopContainer.Bounds.Height;
            Scroll.Height = h >= 0 ? h : 0;
        };

        // Use ScrollViewer.ScrollChanged to determine TopContainer's visibility.
        AddHandlersForTopContainer();

        // PanGestures are not recognized on a ScrollViewer if it contains labs:Swipe
        // items, so we handle the pull with ScrollGestures, ...
        AddHandlersForTouchPull();

        // ... but pulling down with the mouse is not recognized as a ScrollGesture, so
        // we also handle PanGestures.
        AddHandlersForMousePull();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Container.Margin = new Thickness(0, -PullMenu.Bounds.Height - 20, 0, 0);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Scroll.Height = Math.Max(0, Bounds.Height - TopContainer.Bounds.Height);
    }

    #region Styled properties
    public static readonly StyledProperty<IEnumerable> ItemsProperty =
        AvaloniaProperty.Register<PullableMenuScrollViewer, IEnumerable>(nameof(Items));

    public IEnumerable Items
    {
        get => this.GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
    public static readonly StyledProperty<IEnumerable> MenuItemsProperty =
            AvaloniaProperty.Register<PullableMenuScrollViewer, IEnumerable>(nameof(MenuItems));

    public IEnumerable MenuItems
    {
        get => this.GetValue(MenuItemsProperty);
        set => SetValue(MenuItemsProperty, value);
    }

    public static readonly StyledProperty<Control> TopContentProperty =
            AvaloniaProperty.Register<PullableMenuScrollViewer, Control>(nameof(TopContent));

    public Control TopContent
    {
        get => this.GetValue(TopContentProperty);
        set => SetValue(TopContentProperty, value);
    }

    #endregion

    private void HandlePartiallyVisibleTopContainer(object? sender, RoutedEventArgs eventArgs)
    {
        if (TopContainer.Margin.Top < -TopContainer.Bounds.Height / 2 &&
                TopContainer.Margin.Top >= -TopContainer.Bounds.Height)
        {
            TopContainer.Margin = new Thickness(0, -TopContainer.Bounds.Height, 20, 0);
            TopContainer.Opacity = 0;
        }
        else
        {
            TopContainer.Margin = new Thickness(0, 0, 20, 0);
            TopContainer.Opacity = 1;
        }

        eventArgs.Handled = false;
    }

    private void AddHandlersForTopContainer()
    {
        Scroll.ScrollChanged += (sender, e) =>
        {
            if (e.OffsetDelta.Y == 0)
            {
                return;
            }

            double newTop = 0;
            double newHeight = 0;
            var scrollDirectionChanged = (sameDirectionTotalScrolled < 0) != (e.OffsetDelta.Y < 0);
            sameDirectionTotalScrolled = scrollDirectionChanged ? e.OffsetDelta.Y : sameDirectionTotalScrolled + e.OffsetDelta.Y;

            if (Scroll.Offset.Y == 0 && e.OffsetDelta.Y < 0)
            {
                // Make sure to show TopContainer fully when we are at the top.
                TopContainer.Margin = new Thickness(0, 0, 20, 0);
                Scroll.Height = Bounds.Height - TopContainer.Bounds.Height;
            }
            else if (TopContainer.Margin.Top < 0 && TopContainer.Margin.Top > -TopContainer.Bounds.Height)
            {
                // TopContainer is already visible, so we just adjust its position regardless of scroll direction.
                newTop = TopContainer.Margin.Top - e.OffsetDelta.Y;
                newHeight = Scroll.Height;
            }
            else if (sameDirectionTotalScrolled > 0 && TopContainer.Margin.Top >= 0)
            {
                // Scrolling down while TopContainer is fully visible, so we immediately start to slide it out.
                newTop = -sameDirectionTotalScrolled;
                newHeight = Bounds.Height + sameDirectionTotalScrolled;
            }
            else if (sameDirectionTotalScrolled < -100 && TopContainer.Margin.Top <= -TopContainer.Bounds.Height)
            {
                // Scrolling up while TopContainer is not visible, so we start to slide it in after
                // a certain amount (100 pixels) of scrolling.
                var topContainerVisibleSize = -sameDirectionTotalScrolled - 100;
                newTop = -TopContainer.Bounds.Height + topContainerVisibleSize;
                newHeight = Bounds.Height - topContainerVisibleSize;
            }
            else
            {
                // None of the above scenarios happened, so we don't want to adjust TopContainer's position.
                return;
            }

            // Restrict the calculated values for TopContainer's top margin and Scroll's height to proper
            // boundaries, and set these new values.
            newTop = Math.Min(newTop, 0);
            newTop = Math.Max(newTop, -TopContainer.Bounds.Height);
            TopContainer.Margin = new Thickness(0, newTop, 20, 0);
            newHeight = Math.Min(newHeight, Bounds.Height);
            newHeight = Math.Max(newHeight, Bounds.Height - TopContainer.Bounds.Height);
            Scroll.Height = newHeight;

            // Set TopContainer's opacity based on how much of it is visible.
            var opacity = 1 + TopContainer.Margin.Top / TopContainer.Bounds.Height;
            TopContainer.Opacity = Math.Max(0, Math.Min(opacity, 1));
        };

        Scroll.AddHandler(Gestures.ScrollGestureEndedEvent, HandlePartiallyVisibleTopContainer);
        Scroll.PointerMoved += HandlePartiallyVisibleTopContainer;
    }

    private void AddHandlersForTouchPull()
    {
        // When we scroll down while being at the top of the scroll viewer, we
        // move the whole control down.
        Scroll.AddHandler(Gestures.ScrollGestureEvent, (s, e) =>
        {
            if (Scroll.Offset.Y == 0)
            {
                Scroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                totalPulled += -e.Delta.Y / pullRatio;
                PullUpdate();
            }
        });

        // Move the control back to its original place when we stopped pulling.
        Scroll.AddHandler(Gestures.ScrollGestureEndedEvent, (s, e) =>
        {
            if (totalPulled != 0)
            {
                Scroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                PullFinished();
            }
        });
    }

    private void AddHandlersForMousePull()
    {
        var panGestureRecognizer = new PanGestureRecognizer
        {
            Direction = PanDirection.Down | PanDirection.Up,
            Threshold = 10,
        };

        panGestureRecognizer.OnPan += (s, e) =>
        {
            if (Scroll.Offset.Y == 0)
            {
                if (e.StatusType == PanGestureStatus.Completed)
                {
                    PullFinished();
                }
                else
                {
                    totalPulled = e.TotalY / pullRatio;
                    PullUpdate();
                }
            }
        };
        Scroll.GestureRecognizers.Add(panGestureRecognizer);
    }

    private void PullUpdate()
    {
        PullMenu.IsVisible = true;
        Container.Margin = new Thickness(0, -PullMenu.Bounds.Height - 20 + totalPulled, 0, 0);
        Scroll.Height = Bounds.Height - TopContainer.Bounds.Height - totalPulled;

        int? index = (int)(totalPulled - 15) / (int)menuItemHeight - 1;
        index = index < 0 ? null : index;
        if (index != selectedIndex)
        {
            if (selectedIndex is not null &&
                selectedIndex < PullMenu.ItemsSourceView!.Count &&
                PullMenu.ItemsSourceView![PullMenu.ItemsSourceView.Count - selectedIndex.Value - 1] is PullMenuItemViewModel prev)
            {
                prev.Selected = false;
            }

            if (index is not null &&
                index < PullMenu.ItemsSourceView!.Count &&
                PullMenu.ItemsSourceView![PullMenu.ItemsSourceView.Count - index.Value - 1] is PullMenuItemViewModel curr &&
                curr.Command is not null &&
                curr.Command.CanExecute(curr.CommandParameter))
            {
                hapticFeedback?.LongPress();
                curr.Selected = true;
            }

            selectedIndex = index;
        }
    }

    private void PullFinished()
    {
        PullMenu.IsVisible = false;
        if (selectedIndex is not null &&
            selectedIndex < PullMenu.ItemsSourceView!.Count &&
            PullMenu.ItemsSourceView![PullMenu.ItemsSourceView.Count - selectedIndex.Value - 1] is PullMenuItemViewModel selected &&
            selected.Command is not null &&
            selected.Command.CanExecute(selected.CommandParameter))
        {
            selected.Command.Execute(selected.CommandParameter);
            selected.Selected = false;
        }

        totalPulled = 0;
        selectedIndex = null;

        Scroll.Transitions!.Clear();
        Container.Transitions!.Clear();
        Container.Transitions!.Add(marginTransition);
        Scroll.Transitions!.Add(heightTransition);

        Container.Margin = new Thickness(0, -PullMenu.Bounds.Height - 20, 0, 0);
        Scroll.Height = Bounds.Height - TopContainer.Bounds.Height;

        new Thread(() => 
        {
            Thread.Sleep(200);
            Dispatcher.UIThread.Post(new Action(() => {
                Scroll.Transitions!.Clear();
                Container.Transitions!.Clear();
            }));
        }).Start();
    }
}