using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Sufni.App.Views;

public partial class ImportSessionsContentView : UserControl
{
    public static readonly StyledProperty<object?> BottomContentProperty =
        AvaloniaProperty.Register<ImportSessionsContentView, object?>(nameof(BottomContent));

    public static readonly StyledProperty<bool> EnableMalformedMessageTapProperty =
        AvaloniaProperty.Register<ImportSessionsContentView, bool>(nameof(EnableMalformedMessageTap));

    public object? BottomContent
    {
        get => GetValue(BottomContentProperty);
        set => SetValue(BottomContentProperty, value);
    }

    public bool EnableMalformedMessageTap
    {
        get => GetValue(EnableMalformedMessageTapProperty);
        set => SetValue(EnableMalformedMessageTapProperty, value);
    }

    public ImportSessionsContentView()
    {
        InitializeComponent();
    }

    private void ActionSelector_OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateImportActionRow(sender as ComboBox);
    }

    private void ActionSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateImportActionRow(sender as ComboBox);
    }

    private static void UpdateImportActionRow(ComboBox? selector)
    {
        if (selector is null)
        {
            return;
        }

        var selectedIndex = selector.SelectedIndex;

        var row = selector.GetLogicalAncestors().OfType<Expander>().FirstOrDefault()
            ?? selector.FindAncestorOfType<Expander>();
        if (row is not null)
        {
            SetActionClasses(row, selectedIndex);
        }

        var header = selector.GetLogicalAncestors()
            .OfType<Grid>()
            .FirstOrDefault(grid => grid.Classes.Contains("importactionrowheader"))
            ?? selector.GetVisualAncestors()
            .OfType<Grid>()
            .FirstOrDefault(grid => grid.Classes.Contains("importactionrowheader"));
        if (header is not null)
        {
            SetActionClasses(header, selectedIndex);
        }
    }

    private static void SetActionClasses(Control control, int selectedIndex)
    {
        control.Classes.Set("import", selectedIndex == 1);
        control.Classes.Set("trash", selectedIndex == 2);
    }
}
