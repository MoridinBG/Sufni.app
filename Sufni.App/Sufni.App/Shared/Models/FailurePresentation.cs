using System;

namespace Sufni.App.Shared.Models;

/// <summary>Fixed user-facing text with separate, optional diagnostic details.</summary>
public sealed record FailurePresentation
{
    public FailurePresentation(string summary, string? details = null)
    {
        Summary = summary;
        Details = details;
    }

    public string Summary { get; }
    public string? Details { get; }
    public bool HasDetails => !string.IsNullOrEmpty(Details);

    public static FailurePresentation From(StartupFailureReason reason, string? details = null) => new(reason switch
    {
        StartupFailureReason.Database => "The database could not be opened. Reset it to start with an empty database; your current database will be archived.",
        StartupFailureReason.Startup => "The application could not start. See the log for details.",
        StartupFailureReason.Reset => "The database could not be reset. See the log for details.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(VideoImportFailureReason reason, string? details = null) => new(reason switch
    {
        VideoImportFailureReason.LocalFileRequired => "Selected video must be a local file.",
        VideoImportFailureReason.InvalidMetadata => "The selected video could not be read. Choose a video with a recording time and duration.",
        VideoImportFailureReason.Import => "The video could not be imported. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(VideoAlignmentFailureReason reason, string? details = null) => new(reason switch
    {
        VideoAlignmentFailureReason.Save => "Session video alignment could not be saved. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
}
