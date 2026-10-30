using System;

namespace Sufni.App.Shell.Coordinators;

/// <summary>Identifies an editor surface without exposing its view model.</summary>
public abstract record EditorKey
{
    private EditorKey() { }

    public sealed record Bike(Guid Id) : EditorKey;
    public sealed record NewBike : EditorKey;
    public sealed record Setup(Guid Id) : EditorKey;
    public sealed record NewSetup(Guid? SuggestedBoardId = null) : EditorKey;
    public sealed record Session(Guid Id) : EditorKey;
    public sealed record ImportSessions : EditorKey;
    public sealed record LiveDaq(string IdentityKey) : EditorKey;
    public sealed record LiveSession(string IdentityKey) : EditorKey;
}
