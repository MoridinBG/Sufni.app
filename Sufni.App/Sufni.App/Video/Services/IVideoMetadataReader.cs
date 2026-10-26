using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Video.Services;

public interface IVideoMetadataReader
{
    Task<VideoMetadataReadResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default);
}

public sealed record VideoMetadata(DateTimeOffset StartUtc, TimeSpan Duration)
{
    public DateTimeOffset EndUtc => StartUtc + Duration;
}

public abstract record VideoMetadataReadResult
{
    private VideoMetadataReadResult() { }

    public sealed record Parsed(VideoMetadata Metadata) : VideoMetadataReadResult;
    public sealed record Invalid(string ErrorMessage) : VideoMetadataReadResult;
}
