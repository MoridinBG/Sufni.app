namespace Sufni.App.Tests.TestSupport.Fixtures;

public static class TestImages
{
    // 1x1 transparent PNG.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgAAIAAAUAAen63N" +
        "kAAAAASUVORK5CYII=");

    public static byte[] SmallPngBytes() => [.. OnePixelPng];
}
