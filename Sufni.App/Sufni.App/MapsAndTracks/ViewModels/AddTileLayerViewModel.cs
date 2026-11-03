using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.MapsAndTracks.ViewModels;

public sealed partial class AddTileLayerViewModel : ObservableObject, IContentDialogCompletionSource
{
    public static DialogOptions DialogOptions { get; } = new(
        "Add Custom Layer", 400, 350, 0, 0, false,
        OverlayMaxWidth: 420, OverlayMaxHeight: double.PositiveInfinity, OverlayMargin: 16);

    [ObservableProperty] public partial string? Name { get; set; }
    [ObservableProperty] public partial string? UrlTemplate { get; set; }
    [ObservableProperty] public partial string? AttributionText { get; set; }
    [ObservableProperty] public partial string? AttributionUrl { get; set; }

    public TileLayerConfig? ConfirmedLayer { get; private set; }
    public event EventHandler? Completed;

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(UrlTemplate)) return;
        ConfirmedLayer = new TileLayerConfig
        {
            Name = Name,
            UrlTemplate = UrlTemplate,
            AttributionText = AttributionText ?? "",
            AttributionUrl = AttributionUrl ?? "",
            MaxZoom = 24,
            IsCustom = true,
        };
        Completed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Completed?.Invoke(this, EventArgs.Empty);
}
