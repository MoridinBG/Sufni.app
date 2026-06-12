using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Sufni.App.Models;
using Sufni.App.Theming;
using Sufni.App.Views;
using Sufni.App.Views.Controls;
using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.Services;

public class DialogService : IDialogService, IDialogHost, IExtensionDialogService
{
    private readonly ViewLocator viewLocator;
    private Window? owner;
    private Control? overlayHost;
    private DialogPresentationMode presentationMode = DialogPresentationMode.Window;

    public DialogService()
        : this(new ViewLocator())
    {
    }

    public DialogService(ViewLocator viewLocator)
    {
        this.viewLocator = viewLocator;
    }

    public void SetOwner(Window owner)
    {
        this.owner = owner;
    }

    public void SetOverlayHost(Control host)
    {
        overlayHost = host;
    }

    public void SetPresentationMode(DialogPresentationMode mode)
    {
        presentationMode = mode;
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
        return presentationMode == DialogPresentationMode.Window
            ? ShowAddTileLayerWindowAsync()
            : ShowAddTileLayerOverlayAsync();
    }

    public Task<PromptResult> ShowContentDialogAsync(object contentViewModel, DialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(contentViewModel);
        ArgumentNullException.ThrowIfNull(options);

        return presentationMode == DialogPresentationMode.Window
            ? ShowContentDialogWindowAsync(contentViewModel, options)
            : ShowContentDialogOverlayAsync(contentViewModel, options);
    }

    public Task<TResult?> ShowDialogAsync<TResult>(ExtensionDialogRequest<TResult> request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return presentationMode == DialogPresentationMode.Window
            ? ShowExtensionDialogWindowAsync(request)
            : ShowExtensionDialogOverlayAsync(request);
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

    private Task<PromptResult> ShowContentDialogWindowAsync(object contentViewModel, DialogOptions options)
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");

        var dialogOwner = owner ?? throw new InvalidOperationException("Dialog owner has not been set.");
        var tcs = new TaskCompletionSource<PromptResult>();
        var window = new Window
        {
            Title = options.Title,
            Width = options.Width,
            Height = options.Height,
            MinWidth = options.MinWidth,
            MinHeight = options.MinHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = options.CanResize
        };

        window.Content = CreateContentDialogContent(contentViewModel);

        if (contentViewModel is IContentDialogCompletionSource completionSource)
        {
            completionSource.Completed += ContentCompleted;
        }

        window.Closed += (_, _) =>
        {
            if (contentViewModel is IContentDialogCompletionSource completionSource)
            {
                completionSource.Completed -= ContentCompleted;
            }

            tcs.TrySetResult(PromptResult.Cancel);
        };

        window.ShowDialog(dialogOwner);
        return tcs.Task;

        void ContentCompleted(object? sender, EventArgs args)
        {
            if (contentViewModel is IContentDialogCompletionSource completionSource)
            {
                completionSource.Completed -= ContentCompleted;
            }

            tcs.TrySetResult(PromptResult.Ok);
            window.Close();
        }
    }

    private Task<PromptResult> ShowContentDialogOverlayAsync(object contentViewModel, DialogOptions options)
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

        var tcs = new TaskCompletionSource<PromptResult>();
        Control? overlay = null;
        overlay = CreateContentDialogOverlay(CreateContentDialogContent(contentViewModel), options);

        if (contentViewModel is IContentDialogCompletionSource completionSource)
        {
            completionSource.Completed += ContentCompleted;
        }

        overlay.DetachedFromVisualTree += OverlayDetached;
        panel.Children.Add(overlay);
        return tcs.Task;

        void ContentCompleted(object? sender, EventArgs args)
        {
            Complete(PromptResult.Ok);
        }

        void OverlayDetached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            overlay = null;
            if (contentViewModel is IContentDialogCompletionSource completionSource)
            {
                completionSource.Completed -= ContentCompleted;
            }

            tcs.TrySetResult(PromptResult.Cancel);
        }

        void Complete(PromptResult result)
        {
            if (contentViewModel is IContentDialogCompletionSource completionSource)
            {
                completionSource.Completed -= ContentCompleted;
            }

            if (overlay is { } currentOverlay)
            {
                currentOverlay.DetachedFromVisualTree -= OverlayDetached;
                panel.Children.Remove(currentOverlay);
                overlay = null;
            }

            tcs.TrySetResult(result);
        }
    }

    private Task<TResult?> ShowExtensionDialogWindowAsync<TResult>(ExtensionDialogRequest<TResult> request)
    {
        Debug.Assert(owner != null, nameof(owner) + " != null");

        var dialogOwner = owner ?? throw new InvalidOperationException("Dialog owner has not been set.");
        var tcs = new TaskCompletionSource<TResult?>();
        var window = new Window
        {
            Title = request.Title,
            Width = request.Layout.Width,
            Height = request.Layout.Height,
            MinWidth = request.Layout.MinWidth,
            MinHeight = request.Layout.MinHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = request.Layout.CanResize,
            Content = new ContentControl
            {
                Content = request.ViewModel
            }
        };

        request.ViewModel.Completed += ViewModelCompleted;
        window.Closed += WindowClosed;
        window.ShowDialog(dialogOwner);
        return tcs.Task;

        void ViewModelCompleted(object? sender, TResult? result)
        {
            request.ViewModel.Completed -= ViewModelCompleted;
            tcs.TrySetResult(result);
            window.Close();
        }

        void WindowClosed(object? sender, EventArgs args)
        {
            request.ViewModel.Completed -= ViewModelCompleted;
            tcs.TrySetResult(default);
        }
    }

    private Task<TResult?> ShowExtensionDialogOverlayAsync<TResult>(ExtensionDialogRequest<TResult> request)
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

        var tcs = new TaskCompletionSource<TResult?>();
        var content = new ContentControl
        {
            Content = request.ViewModel
        };
        Control? overlay = null;
        overlay = CreateExtensionDialogOverlay(request, content, CloseOverlay);

        request.ViewModel.Completed += ViewModelCompleted;
        overlay.DetachedFromVisualTree += OverlayDetached;
        panel.Children.Add(overlay);
        return tcs.Task;

        void ViewModelCompleted(object? sender, TResult? result)
        {
            Complete(result);
        }

        void CloseOverlay()
        {
            Complete(default);
        }

        void OverlayDetached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            request.ViewModel.Completed -= ViewModelCompleted;
            overlay = null;
            tcs.TrySetResult(default);
        }

        void Complete(TResult? result)
        {
            request.ViewModel.Completed -= ViewModelCompleted;
            if (overlay is { } currentOverlay)
            {
                currentOverlay.DetachedFromVisualTree -= OverlayDetached;
                panel.Children.Remove(currentOverlay);
                overlay = null;
            }

            tcs.TrySetResult(result);
        }
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
                    Background = SufniBrushes.OverlayScrim()
                },
                new Border
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(16),
                    MaxWidth = 420,
                    Background = SufniBrushes.DialogSurface(),
                    CornerRadius = new CornerRadius(6),
                    Child = content
                }
            }
        };
    }

    private Control CreateContentDialogContent(object contentViewModel)
    {
        var content = viewLocator.Build(contentViewModel) ?? new TextBlock
        {
            Text = contentViewModel.GetType().FullName
        };
        content.DataContext = contentViewModel;
        return content;
    }

    private static Control CreateContentDialogOverlay(Control content, DialogOptions options)
    {
        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new Border
                {
                    Background = SufniBrushes.OverlayScrim()
                },
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12),
                    MaxWidth = options.OverlayMaxWidth ?? options.Width,
                    MaxHeight = options.OverlayMaxHeight ?? options.Height,
                    Background = SufniBrushes.DialogSurface(),
                    CornerRadius = new CornerRadius(6),
                    Child = content
                }
            }
        };
    }

    private static Control CreateExtensionDialogOverlay<TResult>(
        ExtensionDialogRequest<TResult> request,
        Control content,
        Action close)
    {
        var closeButton = new Button
        {
            Name = "ExtensionDialogCloseButton",
            Content = "X",
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeButton.Click += (_, _) => close();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 12, 12, 8),
            Children =
            {
                new TextBlock
                {
                    Text = request.Title,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                closeButton
            }
        };
        Grid.SetColumn(closeButton, 1);

        var dialogContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                header,
                new ScrollViewer
                {
                    Margin = new Thickness(16, 0, 16, 16),
                    Content = content
                }
            }
        };
        Grid.SetRow(dialogContent.Children[1], 1);

        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new Border
                {
                    Background = SufniBrushes.OverlayScrim()
                },
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = request.Layout.Width,
                    MaxHeight = request.Layout.Height,
                    Margin = new Thickness(12),
                    Background = SufniBrushes.DialogSurface(),
                    CornerRadius = new CornerRadius(6),
                    Child = dialogContent
                }
            }
        };
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        if (presentationMode == DialogPresentationMode.Overlay)
        {
            return await ShowConfirmationOverlayAsync(title, message);
        }

        Debug.Assert(owner != null, nameof(owner) + " != null");

        var dialog = new OkCancelDialogWindow(title, message);
        var result = await dialog.ShowDialogAsync(owner);
        return result == PromptResult.Ok;
    }

    private Task<bool> ShowConfirmationOverlayAsync(string title, string message)
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

        var tcs = new TaskCompletionSource<bool>();
        Control? overlay = null;
        overlay = CreateConfirmationOverlay(title, message, Complete);
        overlay.DetachedFromVisualTree += OverlayDetached;
        panel.Children.Add(overlay);
        return tcs.Task;

        void OverlayDetached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            overlay = null;
            tcs.TrySetResult(false);
        }

        void Complete(bool result)
        {
            if (overlay is { } currentOverlay)
            {
                currentOverlay.DetachedFromVisualTree -= OverlayDetached;
                panel.Children.Remove(currentOverlay);
                overlay = null;
            }

            tcs.TrySetResult(result);
        }
    }

    private static Control CreateConfirmationOverlay(string title, string message, Action<bool> complete)
    {
        var cancelButton = new Button
        {
            Content = "Cancel"
        };
        cancelButton.Click += (_, _) => complete(false);

        var okButton = new Button
        {
            Content = "OK"
        };
        okButton.Classes.Add("accent");
        okButton.Click += (_, _) => complete(true);

        var buttons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                cancelButton,
                okButton
            }
        };

        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new Border
                {
                    Background = SufniBrushes.OverlayScrim()
                },
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 420,
                    Margin = new Thickness(12),
                    Padding = new Thickness(16),
                    Background = SufniBrushes.DialogSurface(),
                    CornerRadius = new CornerRadius(6),
                    Child = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                FontWeight = FontWeight.SemiBold,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = message,
                                TextWrapping = TextWrapping.Wrap
                            },
                            buttons
                        }
                    }
                }
            }
        };
    }
}
