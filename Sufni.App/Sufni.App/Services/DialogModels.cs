using System;

namespace Sufni.App.Services;

public enum PromptResult { Yes, No, Ok, Cancel }

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
