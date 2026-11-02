using System;
using System.IO;
using System.Reactive.Disposables;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Serilog;
using Sufni.App.Infrastructure;

namespace Sufni.App.Video.Services;

/// <summary>Persists file grants and lends access for a presence check or native player lifetime.</summary>
public interface IVideoFileAccess
{
    Task<VideoAccessSaveResult> RememberAsync(IStorageFile file, string path, CancellationToken cancellationToken = default);
    Task<VideoFileAccessLease?> AcquireAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class VideoFileAccessLease(string path, IDisposable access) : IDisposable
{
    public string Path { get; } = path;
    public void Dispose() => access.Dispose();
}

public abstract record VideoAccessSaveResult
{
    private VideoAccessSaveResult() { }
    public sealed record Saved : VideoAccessSaveResult;
    public sealed record Failed(string? Details = null) : VideoAccessSaveResult;
}

internal sealed class LocalVideoFileAccess(IBackgroundTaskRunner background) : IVideoFileAccess
{
    public Task<VideoAccessSaveResult> RememberAsync(IStorageFile file, string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<VideoAccessSaveResult>(new VideoAccessSaveResult.Saved());

    public Task<VideoFileAccessLease?> AcquireAsync(string path, CancellationToken cancellationToken = default) =>
        background.RunAsync(() => OpenLocal(path), cancellationToken);

    internal static VideoFileAccessLease? OpenLocal(string path)
    {
        try
        {
            return new VideoFileAccessLease(path, File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Uses the picker provider's opaque bookmarks; registered by the iOS head.</summary>
public sealed class BookmarkedVideoFileAccess(
    IFilesService files,
    ISecureStorage storage,
    IBackgroundTaskRunner background) : IVideoFileAccess
{
    private static readonly ILogger logger = Log.ForContext<BookmarkedVideoFileAccess>();

    public async Task<VideoAccessSaveResult> RememberAsync(IStorageFile file, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await background.RunAsync<VideoAccessSaveResult>(async () =>
            {
                var bookmark = file.CanBookmark ? await file.SaveBookmarkAsync() : null;
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(bookmark))
                {
                    logger.Warning("Video access could not be retained for {Path}", path);
                    return new VideoAccessSaveResult.Failed();
                }
                await storage.SetStringAsync(Key(path), bookmark);
                return new VideoAccessSaveResult.Saved();
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Video access could not be saved for {Path}", path);
            return new VideoAccessSaveResult.Failed(exception.Message);
        }
    }

    public async Task<VideoFileAccessLease?> AcquireAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await background.RunAsync(async () =>
            {
                var bookmark = await storage.GetStringAsync(Key(path));
                if (string.IsNullOrEmpty(bookmark)) return LocalVideoFileAccess.OpenLocal(path);
                var file = await files.OpenBookmarkedVideoAsync(bookmark, cancellationToken);
                if (file is null) return null;
                Stream? stream = null;
                try
                {
                    var resolvedPath = file.TryGetLocalPath();
                    if (string.IsNullOrEmpty(resolvedPath))
                    {
                        file.Dispose();
                        return null;
                    }
                    // Avalonia iOS keeps the security-scoped grant alive until this stream closes.
                    stream = await file.OpenReadAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    return new VideoFileAccessLease(resolvedPath, new CompositeDisposable(stream, file));
                }
                catch
                {
                    stream?.Dispose();
                    file.Dispose();
                    throw;
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Video access could not be restored for {Path}", path);
            return null;
        }
    }

    private static string Key(string path) => "VideoBookmark:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
}
