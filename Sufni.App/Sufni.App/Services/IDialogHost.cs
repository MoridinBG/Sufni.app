using Avalonia.Controls;

namespace Sufni.App.Services;

public enum DialogPresentationMode
{
    Window,
    Overlay
}

/// <summary>
/// Host wiring for dialog presentation. Only the application shell wires
/// these during lifetime setup; view models consume
/// <see cref="IDialogService"/> and never see host controls.
/// </summary>
public interface IDialogHost
{
    public void SetOwner(Window owner);
    public void SetOverlayHost(Control host);
    public void SetPresentationMode(DialogPresentationMode mode);
}
