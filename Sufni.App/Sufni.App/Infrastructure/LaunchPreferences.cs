using Sufni.App.Infrastructure.Theming;

namespace Sufni.App.Infrastructure;

/// <summary>The preferences snapshot used for the first frame and runtime initialization.</summary>
public sealed class LaunchPreferences
{
    private readonly UiLayoutProfile defaultLayoutProfile;

    public LaunchPreferences(UiLayoutProfile defaultLayoutProfile)
        : this(new AppPreferences.AppPreferencesDocument(), defaultLayoutProfile)
    {
    }

    internal LaunchPreferences(AppPreferences.AppPreferencesDocument document, UiLayoutProfile defaultLayoutProfile)
    {
        Document = document;
        this.defaultLayoutProfile = defaultLayoutProfile;
    }

    // Runtime writes replace this document, preserving the launch snapshot.
    internal AppPreferences.AppPreferencesDocument Document { get; }
    public SufniThemeMode ThemeMode => Document.Theme.GetMode();
    public UiLayoutProfile LayoutProfile => Document.Ui.GetLayoutProfile() ?? defaultLayoutProfile;
}
