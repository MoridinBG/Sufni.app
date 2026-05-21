using Avalonia;
using Avalonia.Controls;
using Avalonia.Labs.Controls;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.Views.Controls;

public partial class SessionSwipeActionButton : UserControl
{
    public SessionSwipeActionButton()
    {
        InitializeComponent();
        SwipeButton.PropertyChanged += OnSwipePropertyChanged;
    }

    private static void OnSwipePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name != "SwipeState" ||
            sender is not Swipe swipe ||
            swipe.DataContext is not SessionRowViewModel row)
        {
            return;
        }

        switch (e.NewValue)
        {
            case SwipeState.LeftVisible:
                if (row.RecalculateCommand.CanExecute(null))
                {
                    row.RecalculateCommand.Execute(null);
                }

                swipe.SwipeState = SwipeState.Hidden;
                break;

            case SwipeState.RightVisible:
                if (row.UndoableDeleteCommand.CanExecute(null))
                {
                    row.UndoableDeleteCommand.Execute(null);
                }

                swipe.SwipeState = SwipeState.Hidden;
                break;
        }
    }
}
