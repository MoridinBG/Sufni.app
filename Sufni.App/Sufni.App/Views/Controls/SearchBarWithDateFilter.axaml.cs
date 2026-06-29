using Avalonia.Controls;
using Avalonia;

namespace Sufni.App.Views.Controls
{
    public partial class SearchBarWithDateFilter : UserControl
    {
        public static readonly StyledProperty<bool> ShowDrawerButtonProperty =
            AvaloniaProperty.Register<SearchBarWithDateFilter, bool>(nameof(ShowDrawerButton), true);

        public bool ShowDrawerButton
        {
            get => GetValue(ShowDrawerButtonProperty);
            set => SetValue(ShowDrawerButtonProperty, value);
        }

        public SearchBarWithDateFilter()
        {
            InitializeComponent();
        }
    }
}
