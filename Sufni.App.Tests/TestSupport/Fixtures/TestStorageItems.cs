using Avalonia.Platform.Storage;
using NSubstitute;
using System.Text;

namespace Sufni.App.Tests.TestSupport.Fixtures;

public static class TestStorageItems
{
    public static IStorageFile TextFile(string name, string contents)
    {
        var file = Substitute.For<IStorageFile>();
        file.Name.Returns(name);
        file.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(contents))));
        return file;
    }

    public static async IAsyncEnumerable<IStorageItem> EnumerateStorageItems(params IStorageItem[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
