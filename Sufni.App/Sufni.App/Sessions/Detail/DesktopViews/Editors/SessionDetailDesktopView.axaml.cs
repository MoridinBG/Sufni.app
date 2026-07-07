using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Sufni.App.Sessions.Detail.DesktopViews.Editors;

public partial class SessionDetailDesktopView : UserControl
{
    public SessionDetailDesktopView()
    {
        InitializeComponent();

        // Opening the session tab must pull keyboard focus off the sessions list:
        // as a ListBox it consumes Space (plot playback toggle) and letter keys
        // such as S (video sync) via type-ahead before the plot/video top-level
        // key handlers see them. Move focus to the outermost shell-level
        // UserControl rather than this view. Focusing this recreated-per-tab root
        // creates a macOS automation peer that is never released on close, which
        // leaked the whole view/VM/telemetry graph once per open; the shell root is
        // a single stable instance, so its peer never accumulates.
        Loaded += (_, _) => FocusShellRoot();
    }

    private void FocusShellRoot()
    {
        Control? shellRoot = null;
        for (var visual = this.GetVisualParent(); visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is UserControl userControl)
            {
                shellRoot = userControl;
            }
        }

        if (shellRoot is null)
        {
            return;
        }

        shellRoot.Focusable = true;
        shellRoot.Focus();
    }
}
