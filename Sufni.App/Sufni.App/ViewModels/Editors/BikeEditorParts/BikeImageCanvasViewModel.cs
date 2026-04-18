using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.BikeEditing;
using Sufni.App.Stores;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.Editors.Bike;

public partial class BikeImageCanvasViewModel : ObservableObject
{
    private const double CanvasPadding = 20;

    private byte[] imageBytes = [];
    private CoordinateList? rearAxlePathData;
    private Bitmap? rotatedImageCache;
    private double rotatedImageCacheAngle = double.NaN;
    private Rect? jointBounds;
    private Rect? wheelBounds;

    [ObservableProperty] private Bitmap? image;
    [ObservableProperty] private double imageRotationDegrees;
    [ObservableProperty] private bool overlayVisible;
    [ObservableProperty] private List<Point> rearAxlePath = [];

    public byte[] ImageBytes => imageBytes;

    public Bitmap? RotatedImage
    {
        get
        {
            if (Image is null) return null;
            if (Math.Abs(ImageRotationDegrees) < 0.01) return Image;

            if (rotatedImageCache is not null && Math.Abs(rotatedImageCacheAngle - ImageRotationDegrees) < 0.001)
            {
                return rotatedImageCache;
            }

            rotatedImageCache = CreateRotatedBitmap(Image, ImageRotationDegrees);
            rotatedImageCacheAngle = ImageRotationDegrees;
            return rotatedImageCache;
        }
    }

    public double RotatedImageLeft => GetImageBounds()?.X ?? 0;

    public double RotatedImageTop => GetImageBounds()?.Y ?? 0;

    public double CanvasWidth => (GetContentBounds()?.Width ?? 0) + 2 * CanvasPadding;

    public double CanvasHeight => (GetContentBounds()?.Height ?? 0) + 2 * CanvasPadding;

    public double ContentOffsetX => -(GetContentBounds()?.X ?? 0) + CanvasPadding;

    public double ContentOffsetY => -(GetContentBounds()?.Y ?? 0) + CanvasPadding;

    partial void OnImageChanged(Bitmap? value)
    {
        InvalidateRotatedImageCache();
        NotifyImagePropertiesChanged();
        NotifyLayoutPropertiesChanged();
    }

    partial void OnImageRotationDegreesChanged(double value)
    {
        InvalidateRotatedImageCache();
        NotifyImagePropertiesChanged();
        NotifyLayoutPropertiesChanged();
    }

    public void ApplySnapshot(byte[]? imageBytes, double imageRotationDegrees)
    {
        this.imageBytes = imageBytes ?? [];
        Image = BikeImageData.Decode(this.imageBytes);
        ImageRotationDegrees = imageRotationDegrees;
    }

    public void ApplyLoadedImage(byte[] imageBytes, Bitmap image)
    {
        this.imageBytes = imageBytes;
        Image = image;
    }

    public void RefreshLayout(Rect? jointBounds, Rect? wheelBounds)
    {
        this.jointBounds = jointBounds;
        this.wheelBounds = wheelBounds;
        NotifyLayoutPropertiesChanged();
    }

    public void SetRearAxlePathData(CoordinateList? rearAxlePathData)
    {
        this.rearAxlePathData = rearAxlePathData;
    }

    public void RefreshRearAxlePath(double? pixelsToMillimeters)
    {
        if (rearAxlePathData is null || Image is null || !pixelsToMillimeters.HasValue)
        {
            RearAxlePath = [];
            return;
        }

        var imageHeight = Image.Size.Height;
        var pixelsToMillimetersValue = pixelsToMillimeters.Value;

        RearAxlePath = rearAxlePathData.Value.X
            .Zip(rearAxlePathData.Value.Y, (x, y) => new Point(
                x / pixelsToMillimetersValue,
                imageHeight - y / pixelsToMillimetersValue))
            .ToList();
    }

    public bool HasChangesComparedTo(BikeSnapshot snapshot) =>
        !MathUtils.AreEqual(ImageRotationDegrees, snapshot.ImageRotationDegrees) ||
        !snapshot.ImageBytes.AsSpan().SequenceEqual(imageBytes);

    private void InvalidateRotatedImageCache()
    {
        rotatedImageCache = null;
        rotatedImageCacheAngle = double.NaN;
    }

    private void NotifyImagePropertiesChanged()
    {
        OnPropertyChanged(nameof(RotatedImage));
        OnPropertyChanged(nameof(RotatedImageLeft));
        OnPropertyChanged(nameof(RotatedImageTop));
    }

    private void NotifyLayoutPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ContentOffsetX));
        OnPropertyChanged(nameof(ContentOffsetY));
    }

    private Rect? GetImageBounds()
    {
        if (Image is null) return null;

        if (Math.Abs(ImageRotationDegrees) < 0.01)
        {
            return new Rect(0, 0, Image.Size.Width, Image.Size.Height);
        }

        var bounds = CoordinateRotation.GetRotatedBounds(
            Image.Size.Width,
            Image.Size.Height,
            ImageRotationDegrees);
        return new Rect(bounds.minX, bounds.minY, bounds.maxX - bounds.minX, bounds.maxY - bounds.minY);
    }

    private Rect? GetContentBounds()
    {
        var bounds = new[]
        {
            GetImageBounds(),
            jointBounds,
            wheelBounds,
        }
        .Where(rect => rect.HasValue)
        .Select(rect => rect!.Value)
        .ToList();

        if (bounds.Count == 0)
        {
            return null;
        }

        var minX = bounds.Min(rect => rect.X);
        var minY = bounds.Min(rect => rect.Y);
        var maxX = bounds.Max(rect => rect.Right);
        var maxY = bounds.Max(rect => rect.Bottom);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Bitmap CreateRotatedBitmap(Bitmap source, double angleDegrees)
    {
        var bounds = CoordinateRotation.GetRotatedBounds(source.Size.Width, source.Size.Height, angleDegrees);
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
            ctx.DrawImage(source, new Rect(0, 0, source.Size.Width, source.Size.Height));
        }

        return renderTarget;
    }
}