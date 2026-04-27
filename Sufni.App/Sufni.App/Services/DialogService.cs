using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Sufni.App.Models;
using Sufni.App.Views;
using Sufni.App.Views.Controls;

namespace Sufni.App.Services;

public class DialogService : IDialogService
{
    private Window? owner;
    private Control? overlayHost;

    public void SetOwner(Window owner)
    {
        this.owner = owner;
    }

    public void SetOverlayHost(Control host)
    {
        overlayHost = host;
    }

    public async Task<PromptResult> ShowCloseConfirmationAsync(bool isSaveEnabled = true)
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");

        DialogWindow dialog;
        if (isSaveEnabled)
        {
            dialog = new YesNoCancelDialogWindow("Save?", "You have unsaved changes. Save before closing?");
        }
        else
        {
            dialog = new OkCancelDialogWindow("Close?",
                "Page cannot be saved due to missing or wrong data. Are you sure you want to close it?");
        }
        return await dialog.ShowDialogAsync(owner);
    }

    public Task<TileLayerConfig?> ShowAddTileLayerDialogAsync()
    {
        return App.Current?.IsDesktop == true
            ? ShowAddTileLayerWindowAsync()
            : ShowAddTileLayerOverlayAsync();
    }

    private Task<TileLayerConfig?> ShowAddTileLayerWindowAsync()
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");

        var dialogOwner = owner ?? throw new InvalidOperationException("Dialog owner has not been set.");
        var tcs = new TaskCompletionSource<TileLayerConfig?>();
        var window = new Window
        {
            Title = "Add Custom Layer",
            Width = 400,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var content = new AddTileLayerView();
        content.Finished += (s, result) =>
        {
            tcs.TrySetResult(result);
            window.Close();
        };

        window.Closed += (_, _) => tcs.TrySetResult(null);

        window.Content = content;

        window.ShowDialog(dialogOwner);
        return tcs.Task;
    }

    private Task<TileLayerConfig?> ShowAddTileLayerOverlayAsync()
    {
        var host = overlayHost ?? TryGetSingleViewOverlayHost();
        Debug.Assert(host != null, nameof(overlayHost) + " != null");

        if (host is null)
        {
            throw new InvalidOperationException("Dialog overlay host has not been set.");
        }

        var panel = TryGetOverlayPanel(host);
        if (panel is null)
        {
            throw new InvalidOperationException("Dialog overlay host does not expose a panel surface.");
        }

        var tcs = new TaskCompletionSource<TileLayerConfig?>();
        var content = new AddTileLayerView();
        var overlay = CreateAddTileLayerOverlay(content);

        content.Finished += (_, result) =>
        {
            panel.Children.Remove(overlay);
            tcs.TrySetResult(result);
        };

        panel.Children.Add(overlay);
        return tcs.Task;
    }

    private static Control? TryGetSingleViewOverlayHost()
    {
        return (Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView as Control;
    }

    private static Panel? TryGetOverlayPanel(Control host)
    {
        return host as Panel ?? (host as ContentControl)?.Content as Panel;
    }

    private static Control CreateAddTileLayerOverlay(AddTileLayerView content)
    {
        return new Grid
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            Children =
            {
                new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#99000000"))
                },
                new Border
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(16),
                    MaxWidth = 420,
                    Background = new SolidColorBrush(Color.Parse("#15191c")),
                    CornerRadius = new CornerRadius(6),
                    Child = content
                }
            }
        };
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");

        var dialog = new OkCancelDialogWindow(title, message);
        var result = await dialog.ShowDialogAsync(owner);
        return result == PromptResult.Ok;
    }
}