namespace Sufni.App.Tests.TestSupport.Io;

internal static class ProtocolStreamIo
{
    public static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, TestContext.Current.CancellationToken)
            .AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        return bytes;
    }
}
