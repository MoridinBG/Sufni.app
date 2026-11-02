using System;
using System.ComponentModel;

namespace Sufni.App.Infrastructure;

public enum UiLayoutProfile
{
    Compact,
    Workspace,
}

public sealed record AppCapabilities(
    bool CanHostSyncServer,
    bool CanPairAsClient,
    bool SupportsMassStorageImport,
    bool SupportsStorageProviderImport,
    bool CanPickFolder,
    bool CanPlayVideo);

public enum CommandModifier
{
    Control,
    Meta,
}

public sealed record InputCapabilities(
    bool HasPointer,
    bool HasTouch,
    bool HasKeyboard,
    bool SupportsLongPressContextMenu,
    CommandModifier CommandModifier);

public interface IAppEnvironment : INotifyPropertyChanged
{
    UiLayoutProfile LayoutProfile { get; }
    AppCapabilities Capabilities { get; }
    InputCapabilities Input { get; }
    void SetLayoutProfile(UiLayoutProfile layoutProfile);
}

public sealed class AppEnvironment : IAppEnvironment
{
    private UiLayoutProfile layoutProfile;

    public AppEnvironment(
        UiLayoutProfile LayoutProfile,
        AppCapabilities Capabilities,
        InputCapabilities Input)
    {
        layoutProfile = LayoutProfile;
        this.Capabilities = Capabilities;
        this.Input = Input;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiLayoutProfile LayoutProfile
    {
        get => layoutProfile;
        private set
        {
            if (layoutProfile == value)
            {
                return;
            }

            layoutProfile = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LayoutProfile)));
        }
    }

    public AppCapabilities Capabilities { get; }

    public InputCapabilities Input { get; }

    public void SetLayoutProfile(UiLayoutProfile layoutProfile)
    {
        LayoutProfile = layoutProfile;
    }
}
