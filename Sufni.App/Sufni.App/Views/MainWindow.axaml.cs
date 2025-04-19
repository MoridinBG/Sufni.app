using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Sufni.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            #if DEBUG
            
            this.AttachDevTools(new KeyGesture(Key.F12, KeyModifiers.Alt));
            
            #endif
        }
    }
}