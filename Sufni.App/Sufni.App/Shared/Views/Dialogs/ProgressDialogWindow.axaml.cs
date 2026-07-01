using System;
using Avalonia.Controls;

using Sufni.App.Infrastructure;
namespace Sufni.App.Shared.Views.Dialogs;

// Desktop (window-mode) determinate progress dialog. Chrome-less and modal so it
// reads as a loading overlay that only the owning code can dismiss (by closing
// it when the reported work finishes).
public partial class ProgressDialogWindow : Window
{
    public ProgressDialogWindow() : this("Working…") { }

    public ProgressDialogWindow(string title)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
    }

    // Applies a progress report to the bar and status line. Reports arrive on the
    // UI thread via the Progress<T> the dialog service hands to the work.
    public void Update(DialogProgress report)
    {
        Progress.Value = double.IsFinite(report.Fraction) ? Math.Clamp(report.Fraction, 0, 1) : 0;
        StatusText.Text = report.Status;
    }
}
