using System.IO;
using Avalonia.Media.Imaging;

namespace Sufni.App.BikeEditing;

internal static class BikeImageData
{
    public static Bitmap? Decode(byte[]? imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(imageBytes, writable: false);
        return new Bitmap(stream);
    }
}