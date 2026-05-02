namespace Sufni.App.Presentation;

public enum SurfaceStateKind
{
    Hidden,
    Loading,
    WaitingForData,
    Ready,
    Error,
}

public enum SurfaceIndicatorKind
{
    None,
    Spinner,
    ErrorIcon,
}

public sealed record SurfacePresentationState(
    SurfaceStateKind Kind,
    string? Message,
    SurfaceIndicatorKind Indicator)
{
    public bool ReservesLayout => Kind != SurfaceStateKind.Hidden;
    public bool IsHidden => Kind == SurfaceStateKind.Hidden;
    public bool IsReady => Kind == SurfaceStateKind.Ready;
    public bool ShowPlaceholder => ReservesLayout && !IsReady;
    public bool ShowOverlay => Kind is SurfaceStateKind.Loading or SurfaceStateKind.WaitingForData or SurfaceStateKind.Error;
    public bool ShowSpinner => Indicator == SurfaceIndicatorKind.Spinner;
    public bool ShowErrorIcon => Indicator == SurfaceIndicatorKind.ErrorIcon;

    public SurfacePresentationState ApplyPlotSelection(bool selected)
    {
        return selected ? this : Hidden;
    }

    public static SurfacePresentationState Hidden { get; } = new(SurfaceStateKind.Hidden, null, SurfaceIndicatorKind.None);

    public static SurfacePresentationState Ready { get; } = new(SurfaceStateKind.Ready, null, SurfaceIndicatorKind.None);

    public static SurfacePresentationState Loading(string? message = null)
    {
        return new SurfacePresentationState(SurfaceStateKind.Loading, message, SurfaceIndicatorKind.Spinner);
    }

    public static SurfacePresentationState WaitingForData(string? message = null)
    {
        return new SurfacePresentationState(SurfaceStateKind.WaitingForData, message, SurfaceIndicatorKind.Spinner);
    }

    public static SurfacePresentationState Error(string? message)
    {
        return new SurfacePresentationState(SurfaceStateKind.Error, message, SurfaceIndicatorKind.ErrorIcon);
    }
}

public enum SessionScreenStateKind
{
    Loading,
    Ready,
    Error,
}

public sealed record SessionScreenPresentationState(
    SessionScreenStateKind Kind,
    string? Message)
{
    public bool IsLoading => Kind == SessionScreenStateKind.Loading;
    public bool IsReady => Kind == SessionScreenStateKind.Ready;
    public bool IsError => Kind == SessionScreenStateKind.Error;

    public static SessionScreenPresentationState Ready { get; } = new(SessionScreenStateKind.Ready, null);

    public static SessionScreenPresentationState Loading(string? message = null)
    {
        return new SessionScreenPresentationState(SessionScreenStateKind.Loading, message);
    }

    public static SessionScreenPresentationState Error(string? message)
    {
        return new SessionScreenPresentationState(SessionScreenStateKind.Error, message);
    }
}