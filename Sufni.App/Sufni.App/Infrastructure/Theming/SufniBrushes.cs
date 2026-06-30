using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Sufni.App.Theming;
namespace Sufni.App.Infrastructure.Theming;

internal static class SufniBrushes
{
    private const string OverlayScrimBrushKey = "SufniOverlayScrimBrush";
    private const string DialogSurfaceBrushKey = "SufniDialogSurfaceBrush";
    private const string SurfacePlaceholderPreviewBrushKey = "SufniSurfacePlaceholderPreviewBrush";

    public static IBrush OverlayScrim() =>
        ResolveBrush(OverlayScrimBrushKey, SufniThemes.Fallback.Palette.OverlayScrim);

    public static IBrush DialogSurface() =>
        ResolveBrush(DialogSurfaceBrushKey, SufniThemes.Fallback.Palette.DialogSurface);

    public static IBrush SurfacePlaceholderPreview() =>
        ResolveBrush(SurfacePlaceholderPreviewBrushKey, SufniThemes.Fallback.Palette.PlaceholderPreviewSurface);

    private static IBrush ResolveBrush(string key, Color fallback)
    {
        var app = Application.Current;
        if (app is not null &&
            app.TryFindResource(key, app.ActualThemeVariant, out var resource))
        {
            return resource switch
            {
                IBrush brush => brush,
                Color color => color.ToBrush(),
                _ => fallback.ToBrush()
            };
        }

        return fallback.ToBrush();
    }
}
