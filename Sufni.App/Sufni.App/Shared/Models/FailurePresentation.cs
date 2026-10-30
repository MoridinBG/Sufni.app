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
    public static FailurePresentation From(BikeAnalysisFailureReason reason, string? details = null) => new(reason switch
    {
        BikeAnalysisFailureReason.Analyze => "The rear suspension analysis could not be shown. Check the bike configuration and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(BikeImportFailureReason reason, string? details = null) => new(reason switch
    {
        BikeImportFailureReason.Import => "The bike could not be imported. Check the selected file and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(BikeExportFailureReason reason, string? details = null) => new(reason switch
    {
        BikeExportFailureReason.Export => "The bike could not be exported. Check the destination and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(BikeImageFailureReason reason, string? details = null) => new(reason switch
    {
        BikeImageFailureReason.Read => "The bike image could not be read. Choose the file again.",
        BikeImageFailureReason.Decode => "The bike image could not be displayed. Choose a supported image file.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(LeverageRatioImportFailureReason reason, string? details = null) => new(reason switch
    {
        LeverageRatioImportFailureReason.Import => "The leverage ratio file could not be imported. Check the selected file and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(PairingFailureReason reason, string? details = null) => new(reason switch
    {
        PairingFailureReason.Request => "Pairing could not be requested. Check the connection and try again.",
        PairingFailureReason.Confirm => "Pairing could not be completed. Try again.",
        PairingFailureReason.Unpair => "The server could not be unpaired. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(DataStoreDiscoveryFailureReason reason, string? details = null) => new(reason switch
    {
        DataStoreDiscoveryFailureReason.Scan => "DAQ drives could not be scanned. Check the connection and try again.",
        DataStoreDiscoveryFailureReason.Connect => "The DAQ could not be opened. Check the connection and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(GpxImportFailureReason reason, string? details = null) => new(reason switch
    {
        GpxImportFailureReason.InvalidFile => "GPX import stopped because a file has no readable track with timestamps. Check the selected files.",
        GpxImportFailureReason.Import => "GPX import could not be completed. Check the selected files and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(MapLayerFailureReason reason, string? details = null) => new(reason switch
    {
        MapLayerFailureReason.Add => "The custom map layer could not be saved. Try again.",
        MapLayerFailureReason.Select => "The map layer selection could not be saved. Select the layer again to retry.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
    public static FailurePresentation From(SessionProcessingFailureReason reason, string? details = null) => new(reason switch
    {
        SessionProcessingFailureReason.InvalidCalibration => "The recording could not be processed. Check the setup's rear sensor and the bike's suspension configuration.",
        SessionProcessingFailureReason.Calibration => "The rear calibration could not be calculated. Try processing the recording again.",
        SessionProcessingFailureReason.NoTravelData => "The recording has no usable calibrated travel channel. Check the sensors and setup.",
        SessionProcessingFailureReason.InvalidRecording => "The travel data could not be processed. Check the source recording.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(SessionRecomputeFailureReason reason, string? details = null) => new(reason switch
    {
        SessionRecomputeFailureReason.Recompute => "The session could not be recomputed. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(LiveSessionSaveFailureReason reason, string? details = null) => new(reason switch
    {
        LiveSessionSaveFailureReason.Save => "The live session could not be saved. Try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);

    public static FailurePresentation From(SessionImportFailureReason reason, string? details = null) => new(reason switch
    {
        SessionImportFailureReason.Import => "The recording could not be imported. Check the source and try again.",
        SessionImportFailureReason.InvalidFile => "The recording cannot be imported because its SST data is invalid.",
        SessionImportFailureReason.Publish => "The recording was imported, but app state could not be refreshed. The source was left unacknowledged.",
        SessionImportFailureReason.Trash => "The recording could not be moved to trash. Check the source and try again.",
        SessionImportFailureReason.Acknowledge => "The recording was imported, but the source could not be marked as imported.",
        SessionImportFailureReason.Batch => "The import could not be completed. Check the setup and source, then try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    }, details);
}
