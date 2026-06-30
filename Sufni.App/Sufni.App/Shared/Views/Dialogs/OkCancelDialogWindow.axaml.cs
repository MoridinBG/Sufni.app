namespace Sufni.App.Shared.Views.Dialogs;

public partial class OkCancelDialogWindow : DialogWindow
{
    public OkCancelDialogWindow() : base("Dialog") { }

    public OkCancelDialogWindow(string title, string message) : base(title)
    {
        InitializeComponent();
        MessageText.Text = message;
    }
}