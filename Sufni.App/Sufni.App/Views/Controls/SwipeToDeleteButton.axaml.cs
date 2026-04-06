using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Labs.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Svg.Skia;
using HapticFeedback;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.Views.Controls;

public partial class SwipeToDeleteButton : UserControl
{
    private bool animationPlayed = false;
    private readonly IHapticFeedback? hapticFeedback = App.Current?.Services?.GetService<IHapticFeedback>();

    public SwipeToDeleteButton()
    {
        InitializeComponent();
        SwipeButton.Children[2].PropertyChanged += async (s, e) =>
        {
            var deleteButton = ((ContentPresenter)SwipeButton.Children[1]).Content as Button;

            if (e.Property.Name == "RenderTransform" && e.NewValue is TransformOperations ops)
            {
                var offset = ops.Operations[0].Matrix.M31;
                var progress = offset / 64.0; // 64 = deleteButton.Width
                deleteButton!.Padding = new Thickness(Math.Min(24.0, 24.0 * progress), 0, 0, 0); // 24 = (deleteButton.Width - width(svg)) / 2.0

                if (deleteButton!.Command is null || !deleteButton!.Command!.CanExecute(false))
                {
                    return;
                }

                var trashcan = deleteButton!.Content as Image;
                trashcan!.Opacity = animationPlayed ? 1.0 : Math.Min(0.5, progress / 2.0);

                if (offset > deleteButton.Width && !animationPlayed)
                {
                    hapticFeedback?.LongPress();
                    deleteButton.SetCurrentValue(Avalonia.Svg.Skia.Svg.CssProperty, ".default { fill: #bf312d; }");

                    animationPlayed = true;
                    var animation = Resources["ImageSizeAnimation"] as Animation;
                    await animation!.RunAsync(trashcan);
                }
                if (offset < deleteButton.Width && animationPlayed)
                {
                    animationPlayed = false;
                    trashcan!.Opacity = 0.5;
                    deleteButton.SetCurrentValue(Avalonia.Svg.Skia.Svg.CssProperty, ".default { fill: #f0f0f0; }");
                }
            }
        };
    }

    public void SwipePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "SwipeState" && sender is Swipe swipe && e.NewValue is SwipeState.LeftVisible)
        {
            var vm = swipe.DataContext as IListItemRow;
            if (vm is not null && vm.UndoableDeleteCommand.CanExecute(false))
            {
                vm.UndoableDeleteCommand.Execute(false);
            }

            swipe.SwipeState = SwipeState.Hidden;
        }
    }
}
