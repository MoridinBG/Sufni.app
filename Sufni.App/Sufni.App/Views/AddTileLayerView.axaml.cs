using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sufni.App.Models;

namespace Sufni.App.Views;

public partial class AddTileLayerView : UserControl
{
    public event EventHandler<TileLayerConfig?>? Finished;

    public AddTileLayerView()
    {
        InitializeComponent();

        var okButton = this.FindControl<Button>("OkButton");
        var cancelButton = this.FindControl<Button>("CancelButton");

        if (okButton != null) okButton.Click += OkButton_Click;
        if (cancelButton != null) cancelButton.Click += CancelButton_Click;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("NameBox")?.Text;
        var url = this.FindControl<TextBox>("UrlTemplateBox")?.Text;
        var attrText = this.FindControl<TextBox>("AttributionBox")?.Text;
        var attrUrl = this.FindControl<TextBox>("AttributionUrlBox")?.Text;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var config = new TileLayerConfig
        {
            Name = name,
            UrlTemplate = url,
            AttributionText = attrText ?? "",
            AttributionUrl = attrUrl ?? "",
            MaxZoom = 24,
            IsCustom = true
        };

        Finished?.Invoke(this, config);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Finished?.Invoke(this, null);
    }
}
