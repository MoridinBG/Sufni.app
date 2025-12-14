using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using System.Windows.Input;

namespace Sufni.App.Views.Controls;

public partial class PinInput : UserControl
{
    public static readonly StyledProperty<string> PinProperty =
        AvaloniaProperty.Register<PinInput, string>(nameof(Pin), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string Pin
    {
        get => GetValue(PinProperty);
        set => SetValue(PinProperty, value);
    }

    public static readonly StyledProperty<ICommand?> PinCompletedCommandProperty =
        AvaloniaProperty.Register<PinInput, ICommand?>(nameof(PinCompletedCommand));

    public ICommand? PinCompletedCommand
    {
        get => GetValue(PinCompletedCommandProperty);
        set => SetValue(PinCompletedCommandProperty, value);
    }

    private readonly TextBox[] boxes;
    private bool updating;

    public PinInput()
    {
        InitializeComponent();

        boxes = [Digit0, Digit1, Digit2, Digit3, Digit4, Digit5];

        for (var i = 0; i < boxes.Length; i++)
        {
            var index = i;
            var box = boxes[i];

            box.TextChanged += (_, __) =>
            {
                if (updating)
                    return;

                if (box.Text?.Length > 1)
                    box.Text = box.Text[..1];

                if (!string.IsNullOrEmpty(box.Text) && !char.IsDigit(box.Text[0]))
                    box.Text = "";

                UpdatePin();

                if (!string.IsNullOrEmpty(box.Text) && index < boxes.Length - 1)
                    boxes[index + 1].Focus();

                CheckCompletion();
            };

            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Back)
                    return;

                if (string.IsNullOrEmpty(box.Text) && index > 0)
                {
                    boxes[index - 1].Text = "";
                    boxes[index - 1].Focus();
                }
                else
                {
                    box.Text = "";
                }

                UpdatePin();
                e.Handled = true;
            };
        }
    }

    private void UpdatePin()
    {
        updating = true;
        Pin = string.Concat(boxes.Select(b => b.Text));
        updating = false;
    }

    private void CheckCompletion()
    {
        if (boxes.All(b => b.Text?.Length == 1) && PinCompletedCommand?.CanExecute(Pin) == true)
        {
            PinCompletedCommand.Execute(Pin);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != PinProperty || updating) return;
        updating = true;
        var value = change.NewValue as string ?? "";
        for (var i = 0; i < boxes.Length; i++)
            boxes[i].Text = i < value.Length ? value[i].ToString() : "";
        updating = false;

        CheckCompletion();
    }
}
