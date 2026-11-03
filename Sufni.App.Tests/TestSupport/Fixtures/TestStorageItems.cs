using Avalonia.Platform.Storage;
using NSubstitute;
using System.Text;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Fixtures;

public static class TestStorageItems
{
    public static IStorageFile TextFile(string name, string contents) =>
        BinaryFile(name, Encoding.UTF8.GetBytes(contents));

    public static IStorageFile BinaryFile(string name, byte[] contents)
    {
        var file = Substitute.For<IStorageFile>();
        file.Name.Returns(name);
        file.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(contents)));
        return file;
    }

    public static PickedFile PickedTextFile(string name, string contents) =>
        new(name, () => OpenTextAsync(contents));

    private static Task<Stream> OpenTextAsync(string contents) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(contents)));

    public static async IAsyncEnumerable<IStorageItem> EnumerateStorageItems(params IStorageItem[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
