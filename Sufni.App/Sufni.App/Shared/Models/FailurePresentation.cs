using System;

namespace Sufni.App.Shared.Models;

/// <summary>Fixed user-facing text with separate, optional diagnostic details.</summary>
public sealed record FailurePresentation
{
    private FailurePresentation(string summary, string? details)
    {
        Summary = summary;
        Details = details;
    }

    public string Summary { get; }
    public string? Details { get; }

    public static FailurePresentation From(StartupFailureReason reason, string? details = null) => new(reason switch
    {
        StartupFailureReason.Database => "The database could not be opened. Reset it to start with an empty database; your current database will be archived.",
        StartupFailureReason.Startup => "The application could not start. See the log for details.",
        StartupFailureReason.Reset => "The database could not be reset. See the log for details.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
}
