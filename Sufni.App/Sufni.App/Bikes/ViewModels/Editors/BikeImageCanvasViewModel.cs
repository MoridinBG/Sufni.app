using System;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed class BikeImageCanvasViewModel : ObservableObject, IDisposable
{
    private BikeEditorImage? installed;
    private BikeEditorImage? current;
    private Bitmap? bitmap;
    private Bitmap? rotated;
    private double rotation;

    public Bitmap? Image => installed == current ? bitmap : null;
    public Bitmap? RotatedImage => Image is not { } source || Math.Abs(rotation) < 0.01 ? Image :
        rotated ??= CreateRotatedBitmap(source, rotation);

    internal void Apply(BikeEditorState state)
    {
        current = state.Phase == BikeEditorPhase.Closed ? null : state.Inputs.Image;
        var changed = current != installed || rotation != state.Inputs.Rotation;
        if (current != installed) Release();
        if (rotation != state.Inputs.Rotation)
        {
            rotated?.Dispose();
            rotated = null;
            rotation = state.Inputs.Rotation;
        }
        if (changed) NotifyImage();
    }

    internal void Install(BikeEditorImage token, Bitmap decoded)
    {
        if (token != current) { decoded.Dispose(); return; }
        Release();
        installed = token;
        bitmap = decoded;
        NotifyImage();
    }

    private void NotifyImage()
    {
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(RotatedImage));
    }

    private void Release()
    {
        rotated?.Dispose();
        rotated = null;
        bitmap?.Dispose();
        bitmap = null;
        installed = null;
    }

    public void Dispose()
    {
        current = null;
        Release();
        NotifyImage();
    }

    private static Bitmap CreateRotatedBitmap(Bitmap source, double angleDegrees)
    {
        var bounds = CoordinateRotation.GetRotatedBounds(source.PixelSize.Width, source.PixelSize.Height, angleDegrees);
        var newWidth = (int)Math.Ceiling(bounds.maxX - bounds.minX);
        var newHeight = (int)Math.Ceiling(bounds.maxY - bounds.minY);

        var offsetX = -bounds.minX;
        var offsetY = -bounds.minY;

        var renderTarget = new RenderTargetBitmap(new PixelSize(newWidth, newHeight));
        using var ctx = renderTarget.CreateDrawingContext();
        var translateMatrix = Matrix.CreateTranslation(offsetX, offsetY);
        var rotateMatrix = Matrix.CreateRotation(angleDegrees * Math.PI / 180.0);
        var combinedMatrix = rotateMatrix * translateMatrix;

        using (ctx.PushTransform(combinedMatrix))
        {
            ctx.DrawImage(source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height));
        }

        return renderTarget;
    }
}
