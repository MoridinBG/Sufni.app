using Avalonia.Platform.Storage;

namespace Sufni.App.Tests.TestSupport;

public static class TestStorageItems
{
    public static async IAsyncEnumerable<IStorageItem> EnumerateStorageItems(params IStorageItem[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
