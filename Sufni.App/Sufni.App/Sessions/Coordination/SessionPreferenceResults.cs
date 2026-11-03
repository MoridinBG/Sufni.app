using Sufni.App.Sessions.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;

namespace Sufni.App.Sessions.Coordination;

public abstract record SessionPreferenceLoadResult
{
    private SessionPreferenceLoadResult() { }
    public sealed record Loaded(SessionPreferences Preferences) : SessionPreferenceLoadResult;
    public sealed record Failed(SessionEditorFailureReason Reason, string? Details) : SessionPreferenceLoadResult;
}

public abstract record SessionPreferenceSaveResult
{
    private SessionPreferenceSaveResult() { }
    public sealed record Saved : SessionPreferenceSaveResult;
    public sealed record Failed(SessionEditorFailureReason Reason, string? Details) : SessionPreferenceSaveResult;
}
