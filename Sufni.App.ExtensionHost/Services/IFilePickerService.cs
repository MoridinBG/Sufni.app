using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Sufni.App.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<IStorageFile>> OpenFilesAsync(
        FilePickerRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record FilePickerRequest(
    string Title,
    bool AllowMultiple,
    IReadOnlyList<FilePickerFilter> FileTypeFilters)
{
    public FilePickerOpenOptions ToOpenOptions()
    {
        var fileTypeFilters = new List<FilePickerFileType>(FileTypeFilters.Count);
        foreach (var filter in FileTypeFilters)
        {
            fileTypeFilters.Add(filter.ToFilePickerFileType());
        }

        return new FilePickerOpenOptions
        {
            Title = Title,
            AllowMultiple = AllowMultiple,
            FileTypeFilter = fileTypeFilters,
        };
    }
}

public sealed record FilePickerFilter(
    string Name,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> MimeTypes,
    IReadOnlyList<string> AppleUniformTypeIdentifiers)
{
    public static FilePickerFilter AllFiles { get; } = new("All files", ["*"], [], []);

    public FilePickerFileType ToFilePickerFileType()
    {
        return new FilePickerFileType(Name)
        {
            Patterns = Patterns,
            MimeTypes = MimeTypes,
            AppleUniformTypeIdentifiers = AppleUniformTypeIdentifiers,
        };
    }
}
