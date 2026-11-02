using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;

namespace Sufni.App.Video.Services;

public interface IVideoImportFileService
{
    Task<VideoFileReadResult> ReadAsync(CancellationToken cancellationToken = default);
}

public abstract record VideoFileReadResult
{
    private VideoFileReadResult() { }
    public sealed record Read(string Name, string Path, VideoMetadata Metadata) : VideoFileReadResult;
    public sealed record Cancelled : VideoFileReadResult;
    public sealed record Failed(VideoImportFailureReason Reason, string? Details = null) : VideoFileReadResult;
}

internal sealed class VideoImportFileService(
    IFilesService files,
    IVideoMetadataReader metadataReader,
    IVideoFileAccess access,
    IBackgroundTaskRunner background) : IVideoImportFileService
{
    private static readonly ILogger logger = Log.ForContext<VideoImportFileService>();

    public async Task<VideoFileReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var file = await files.OpenVideoFileAsync();
            if (file is null) return new VideoFileReadResult.Cancelled();
            return await background.RunAsync<VideoFileReadResult>(async () =>
            {
                var path = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path)) return new VideoFileReadResult.Failed(VideoImportFailureReason.LocalFileRequired);
                path = Path.GetFullPath(path);
                VideoMetadataReadResult metadata;
                await using (var stream = await file.OpenReadAsync())
                    metadata = await metadataReader.ReadAsync(stream, cancellationToken);
                if (metadata is VideoMetadataReadResult.Invalid invalid)
                    return new VideoFileReadResult.Failed(VideoImportFailureReason.InvalidMetadata, invalid.ErrorMessage);
                if (await access.RememberAsync(file, path, cancellationToken) is VideoAccessSaveResult.Failed failed)
                    return new VideoFileReadResult.Failed(VideoImportFailureReason.Access, failed.Details);
                return new VideoFileReadResult.Read(file.Name, path, ((VideoMetadataReadResult.Parsed)metadata).Metadata);
            }, cancellationToken);
        }
        catch (OperationCanceledException) { return new VideoFileReadResult.Cancelled(); }
        catch (Exception exception)
        {
            logger.Error(exception, "Video file could not be prepared for import");
            return new VideoFileReadResult.Failed(VideoImportFailureReason.Import, exception.Message);
        }
    }
}
