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
    public static FailurePresentation From(PairedDeviceUnpairFailureReason reason, string? details = null) => new(reason switch
    {
        PairedDeviceUnpairFailureReason.Unpair => "The device could not be unpaired. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(PendingDeleteFailureReason reason, string? details = null) => new(reason switch
    {
        PendingDeleteFailureReason.Delete => "The item could not be deleted. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(BikeSaveFailureReason reason, string? details = null) => new(reason switch
    {
        BikeSaveFailureReason.Save => "The bike could not be saved. Try again.",
        BikeSaveFailureReason.Analysis => "The rear suspension could not be analyzed. Check the bike configuration.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(BikeDampingCutoffFailureReason reason, string? details = null) => new(reason switch
    {
        BikeDampingCutoffFailureReason.Save => "The damping cutoff could not be saved. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(BikeDeleteFailureReason reason, string? details = null) => new(reason switch
    {
        BikeDeleteFailureReason.Delete => "The bike could not be deleted. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(SetupSaveFailureReason reason, string? details = null) => new(reason switch
    {
        SetupSaveFailureReason.Save => "The setup could not be saved. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(SetupImportFailureReason reason, string? details = null) => new(reason switch
    {
        SetupImportFailureReason.Import => "The setup could not be imported. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(SetupExportFailureReason reason, string? details = null) => new(reason switch
    {
        SetupExportFailureReason.Export => "The setup could not be exported. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(SetupDeleteFailureReason reason, string? details = null) => new(reason switch
    {
        SetupDeleteFailureReason.Delete => "The setup could not be deleted. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(SessionSaveFailureReason reason, string? details = null) => new(reason switch
    {
        SessionSaveFailureReason.Save => "The session could not be saved. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(SessionEditFailureReason reason, string? details = null) => new(reason switch
    {
        SessionEditFailureReason.Edit => "The session could not be edited. Try again.",
        SessionEditFailureReason.Rejected => "The session edit could not be applied. Reload the session and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(GpsOffsetFailureReason reason, string? details = null) => new(reason switch
    {
        GpsOffsetFailureReason.Save => "GPS alignment could not be saved. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(SyncFailureReason reason, string? details = null) => new(reason switch
    {
        SyncFailureReason.Synchronize => "Synchronization could not be completed. Check the connection and try again.",
        SyncFailureReason.ProtocolMismatch => "The devices use different sync versions. Update both devices to the same app version.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
}
