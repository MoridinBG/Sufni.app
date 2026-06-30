using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sufni.App.Shared.Views.Dialogs;

public partial class YesNoCancelDialogWindow : DialogWindow
{
    public YesNoCancelDialogWindow() : this("Confirm", "Yes? No? Cancel?") { }
    public YesNoCancelDialogWindow(string title, string message) : base(title)
    {
        InitializeComponent();
        MessageText.Text = message;
    }
}