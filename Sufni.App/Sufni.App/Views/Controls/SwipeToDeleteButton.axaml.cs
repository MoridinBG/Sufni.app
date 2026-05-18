using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Labs.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;
using Sufni.App.Behaviors;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.Views.Controls;

public partial class SwipeToDeleteButton : UserControl
{
    private readonly SwipeToDeleteGestureAdapter gestureAdapter;

    public SwipeToDeleteButton()
    {
        InitializeComponent();
        gestureAdapter = new SwipeToDeleteGestureAdapter(
            SwipeButton,
            () => Resources["ImageSizeAnimation"] as Animation,
            () => RaiseEvent(new RoutedEventArgs(HapticFeedbackBehavior.LongPressFeedbackRequestedEvent)));
        gestureAdapter.Attach();
    }
}

internal sealed class SwipeToDeleteGestureAdapter(
    Swipe swipe,
    Func<Animation?> deleteIconAnimation,
    Action requestDeleteThresholdFeedback)
{
    private const double DeleteButtonWidth = 64.0;
    private const double DeleteIconCenteredPadding = 24.0;
    private bool animationPlayed;
    private Control? swipeContentSurface;
    private Button? deleteButton;
    private Image? deleteIcon;

    public void Attach()
    {
        swipe.PropertyChanged += OnSwipePropertyChanged;
        swipe.AttachedToVisualTree += OnSwipeAttachedToVisualTree;
        ResolveVisualParts();
    }

    private void OnSwipeAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ResolveVisualParts();
    }

    private void ResolveVisualParts()
    {
        deleteButton = swipe.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "DeleteButton");
        deleteIcon = swipe.GetVisualDescendants()
            .OfType<Image>()
            .FirstOrDefault(image => image.Name == "DeleteIcon");

        var contentSurface = swipe.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .FirstOrDefault(presenter => presenter.Content is Button { Name: "OpenButton" });
        if (ReferenceEquals(swipeContentSurface, contentSurface))
        {
            return;
        }

        if (swipeContentSurface is not null)
        {
            swipeContentSurface.PropertyChanged -= OnSwipeContentPropertyChanged;
        }

        swipeContentSurface = contentSurface;
        if (swipeContentSurface is not null)
        {
            swipeContentSurface.PropertyChanged += OnSwipeContentPropertyChanged;
        }
    }

    private void OnSwipePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        ResolveVisualParts();

        if (e.Property.Name == "SwipeState" && sender is Swipe currentSwipe && e.NewValue is SwipeState.LeftVisible)
        {
            if (currentSwipe.DataContext is ListItemRowViewModelBase vm &&
                vm.UndoableDeleteCommand.CanExecute(false))
            {
                vm.UndoableDeleteCommand.Execute(false);
            }

            currentSwipe.SwipeState = SwipeState.Hidden;
        }
    }

    private async void OnSwipeContentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name != "RenderTransform" ||
            e.NewValue is not TransformOperations ops ||
            ops.Operations.Count == 0)
        {
            return;
        }

        ResolveVisualParts();
        if (deleteButton is null || deleteIcon is null)
        {
            return;
        }

        var offset = ops.Operations[0].Matrix.M31;
        var progress = offset / DeleteButtonWidth;
        deleteButton.Padding = new Thickness(Math.Min(DeleteIconCenteredPadding, DeleteIconCenteredPadding * progress), 0, 0, 0);

        if (deleteButton.Command is null || !deleteButton.Command.CanExecute(false))
        {
            return;
        }

        deleteIcon.Opacity = animationPlayed ? 1.0 : Math.Min(0.5, progress / 2.0);

        if (offset > DeleteButtonWidth && !animationPlayed)
        {
            requestDeleteThresholdFeedback();
            deleteButton.SetCurrentValue(Avalonia.Svg.Skia.Svg.CssProperty, ".default { fill: #bf312d; }");

            animationPlayed = true;
            if (deleteIconAnimation() is { } animation)
            {
                await animation.RunAsync(deleteIcon);
            }
        }
        else if (offset < DeleteButtonWidth && animationPlayed)
        {
            animationPlayed = false;
            deleteIcon.Opacity = 0.5;
            deleteButton.SetCurrentValue(Avalonia.Svg.Skia.Svg.CssProperty, ".default { fill: #f0f0f0; }");
        }
    }
}
