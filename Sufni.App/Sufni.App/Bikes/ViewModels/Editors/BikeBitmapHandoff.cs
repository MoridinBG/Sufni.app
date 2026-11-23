using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Sufni.App.Infrastructure;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal sealed class BikeBitmapHandoff(IBackgroundTaskRunner background, BikeImageCanvasViewModel canvas)
{
    public async Task<bool> DecodeAsync(byte[] bytes, Func<BikeEditorImage, Task<bool>> accept, CancellationToken token)
    {
        var decoded = await background.RunAsync(() => BikeImageData.Decode(bytes) ??
            throw new InvalidOperationException("Image contains no pixels."), token);
        try
        {
            token.ThrowIfCancellationRequested();
            var image = new BikeEditorImage(bytes, new Size(decoded.PixelSize.Width, decoded.PixelSize.Height));
            if (!await accept(image)) return false;
            canvas.Install(image, decoded);
            decoded = null;
            return true;
        }
        finally { decoded?.Dispose(); }
    }
}
