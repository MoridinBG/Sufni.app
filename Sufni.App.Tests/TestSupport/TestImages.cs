using Avalonia.Media.Imaging;

namespace Sufni.App.Tests.Infrastructure;

/// <summary>
/// Tiny embedded PNG decoded into an Avalonia <see cref="Bitmap"/>.
/// Used by editor tests that exercise the rear-suspension code paths,
/// where the bike VM asserts a non-null <c>Image</c>. The image content
/// is irrelevant; only its existence and a usable
/// <see cref="Bitmap.Size"/> matter.
/// </summary>
public static class TestImages
{
    // 1x1 transparent PNG.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgAAIAAAUAAen63N" +
        "kAAAAASUVORK5CYII=");

    public static byte[] SmallPngBytes() => [.. OnePixelPng];

    public static Bitmap SmallPng()
    {
        using var ms = new MemoryStream(OnePixelPng);
        return new Bitmap(ms);
    }
}
