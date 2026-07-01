using System;

namespace Sufni.App.Infrastructure;

public enum PromptResult { Yes, No, Ok, Cancel }

// A single selectable option in a generic multi-choice prompt: a stable id the
// caller switches on, the button label to display, and whether it is the
// accented default action.
public sealed record DialogChoice(string Id, string Label, bool IsDefault = false);

// A single update for a determinate progress dialog: a completion fraction in
// [0, 1] and an optional status line.
public sealed record DialogProgress(double Fraction, string? Status = null);

public sealed record DialogOptions(
    string Title,
    double Width,
    double Height,
    double MinWidth,
    double MinHeight,
    bool CanResize,
    double? OverlayMaxWidth = null,
    double? OverlayMaxHeight = null);

public interface IContentDialogCompletionSource
{
    event EventHandler? Completed;
}
