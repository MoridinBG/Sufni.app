using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.Tests.Services;

public class FilePickerRequestTests
{
    [Fact]
    public void ToOpenOptions_CopiesTitleMultiplicityAndFilters()
    {
        var request = new FilePickerRequest(
            "Open recording",
            AllowMultiple: true,
            [
                new FilePickerFilter(
                    "Recordings",
                    ["*.rec", "*.bin"],
                    ["application/octet-stream"],
                    ["public.data"])
            ]);

        var options = request.ToOpenOptions();

        Assert.Equal("Open recording", options.Title);
        Assert.True(options.AllowMultiple);
        Assert.NotNull(options.FileTypeFilter);
        var filter = Assert.Single(options.FileTypeFilter!);
        Assert.Equal("Recordings", filter.Name);
        Assert.Equal(["*.rec", "*.bin"], filter.Patterns);
        Assert.Equal(["application/octet-stream"], filter.MimeTypes);
        Assert.Equal(["public.data"], filter.AppleUniformTypeIdentifiers);
    }

    [Fact]
    public void AllFilesFilter_ProducesWildcardFileType()
    {
        var fileType = FilePickerFilter.AllFiles.ToFilePickerFileType();

        Assert.Equal("All files", fileType.Name);
        Assert.Equal(["*"], fileType.Patterns);
        Assert.NotNull(fileType.MimeTypes);
        Assert.NotNull(fileType.AppleUniformTypeIdentifiers);
        Assert.Empty(fileType.MimeTypes!);
        Assert.Empty(fileType.AppleUniformTypeIdentifiers!);
    }
}
