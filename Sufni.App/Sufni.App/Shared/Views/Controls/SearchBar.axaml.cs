using Avalonia.Controls;
using Avalonia;

namespace Sufni.App.Shared.Views.Controls
{
    public partial class SearchBar : UserControl
    {
        public static readonly StyledProperty<bool> ShowDrawerButtonProperty =
            AvaloniaProperty.Register<SearchBar, bool>(nameof(ShowDrawerButton), true);

        public bool ShowDrawerButton
        {
            get => GetValue(ShowDrawerButtonProperty);
            set => SetValue(ShowDrawerButtonProperty, value);
        }

        public SearchBar()
        {
            InitializeComponent();
        }
    }
}
