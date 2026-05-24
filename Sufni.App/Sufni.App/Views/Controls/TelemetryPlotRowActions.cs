using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Theming;

namespace Sufni.App.Views.Controls;

public enum TelemetryPlotRowActionKind
{
    Toggle,
    Execute,
}

public enum TelemetryPlotRowActionTone
{
    Default,
    Accent,
    Danger,
}

public sealed partial class TelemetryPlotRowAction : ObservableObject
{
    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private TelemetryPlotRowActionKind kind;
    [ObservableProperty] private Geometry? iconGeometry;
    [ObservableProperty] private object? toolTip;
    [ObservableProperty] private ICommand? command;
    [ObservableProperty] private bool isVisible = true;
    [ObservableProperty] private bool isEnabled = true;
    [ObservableProperty] private bool isChecked;
    [ObservableProperty] private bool isHighlighted;
    [ObservableProperty] private TelemetryPlotRowActionTone tone;
}

internal sealed class TelemetryPlotRowActionsPresenter : UserControl
{
    internal const double ButtonSize = 24;
    internal const double ButtonIconSize = 13;
    internal const double ButtonSpacing = 4;
    internal const double GroupSpacing = 12;

    private readonly StackPanel rootPanel;
    private readonly StackPanel toggleGroup;
    private readonly StackPanel executeGroup;
    private readonly Border groupGap;
    private readonly List<TelemetryPlotRowAction> subscribedActions = [];
    private IDisposable? themeVariantSubscription;
    private INotifyCollectionChanged? subscribedCollection;

    public static readonly StyledProperty<IEnumerable<TelemetryPlotRowAction>?> ActionsProperty =
        AvaloniaProperty.Register<TelemetryPlotRowActionsPresenter, IEnumerable<TelemetryPlotRowAction>?>(
            nameof(Actions));

    public IEnumerable<TelemetryPlotRowAction>? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public TelemetryPlotRowActionsPresenter()
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;

        toggleGroup = new StackPanel
        {
            Name = "ToggleActionsGroup",
            Orientation = Orientation.Horizontal,
            Spacing = ButtonSpacing,
        };
        groupGap = new Border
        {
            Name = "ActionGroupGap",
            Width = GroupSpacing,
            IsHitTestVisible = false,
        };
        executeGroup = new StackPanel
        {
            Name = "ExecuteActionsGroup",
            Orientation = Orientation.Horizontal,
            Spacing = ButtonSpacing,
        };
        rootPanel = new StackPanel
        {
            Name = "TelemetryPlotRowActionsRoot",
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                toggleGroup,
                groupGap,
                executeGroup,
            },
        };

        Content = rootPanel;
        AddHandler<PointerPressedEventArgs>(
            InputElement.PointerPressedEvent,
            (_, args) => args.Handled = true,
            handledEventsToo: true);

        PropertyChanged += (_, args) =>
        {
            if (args.Property == ActionsProperty)
            {
                SubscribeToCollection();
                Rebuild();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        themeVariantSubscription = this.GetObservable(ThemeVariantScope.ActualThemeVariantProperty)
            .Subscribe(_ => Rebuild());
        SubscribeToCollection();
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        themeVariantSubscription?.Dispose();
        themeVariantSubscription = null;
        UnsubscribeFromCollection();
        UnsubscribeFromActions();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToCollection()
    {
        UnsubscribeFromCollection();
        if (Actions is INotifyCollectionChanged observable)
        {
            subscribedCollection = observable;
            observable.CollectionChanged += OnCollectionChanged;
        }
    }

    private void UnsubscribeFromCollection()
    {
        if (subscribedCollection is not null)
        {
            subscribedCollection.CollectionChanged -= OnCollectionChanged;
            subscribedCollection = null;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => Rebuild();

    private void Rebuild()
    {
        UnsubscribeFromActions();
        toggleGroup.Children.Clear();
        executeGroup.Children.Clear();

        var actions = GetVisibleActions().ToArray();
        foreach (var action in actions)
        {
            SubscribeToAction(action);
            var button = CreateActionButton(action);
            switch (action.Kind)
            {
                case TelemetryPlotRowActionKind.Toggle:
                    toggleGroup.Children.Add(button);
                    break;
                case TelemetryPlotRowActionKind.Execute:
                    executeGroup.Children.Add(button);
                    break;
            }
        }

        var hasToggleActions = toggleGroup.Children.Count > 0;
        var hasExecuteActions = executeGroup.Children.Count > 0;
        toggleGroup.IsVisible = hasToggleActions;
        groupGap.IsVisible = hasToggleActions && hasExecuteActions;
        executeGroup.IsVisible = hasExecuteActions;
        IsVisible = hasToggleActions || hasExecuteActions;
    }

    private IEnumerable<TelemetryPlotRowAction> GetVisibleActions()
    {
        if (Actions is null)
        {
            return [];
        }

        return Actions
            .Where(action => action.IsVisible)
            .OrderBy(action => action.Kind);
    }

    private void SubscribeToAction(TelemetryPlotRowAction action)
    {
        subscribedActions.Add(action);
        action.PropertyChanged += OnActionPropertyChanged;
    }

    private void UnsubscribeFromActions()
    {
        foreach (var action in subscribedActions)
        {
            action.PropertyChanged -= OnActionPropertyChanged;
        }

        subscribedActions.Clear();
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs args) => Rebuild();

    private Button CreateActionButton(TelemetryPlotRowAction action)
    {
        var button = new Button
        {
            Name = string.IsNullOrWhiteSpace(action.Id) ? null : action.Id,
            Width = ButtonSize,
            Height = ButtonSize,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Command = action.Command,
            IsEnabled = action.IsEnabled,
            Content = CreateIcon(action),
        };

        ApplyButtonColors(button, action);
        ToolTip.SetTip(button, action.ToolTip);
        return button;
    }

    private static PathIcon CreateIcon(TelemetryPlotRowAction action)
    {
        return new PathIcon
        {
            Width = ButtonIconSize,
            Height = ButtonIconSize,
            Data = action.IconGeometry,
        };
    }

    private void ApplyButtonColors(Button button, TelemetryPlotRowAction action)
    {
        var theme = SufniThemes.FromVariant(ActualThemeVariant);
        var foregroundColor = GetForegroundColor(theme, action);
        var foreground = foregroundColor.ToBrush();
        button.Foreground = foreground;
        if (button.Content is PathIcon icon)
        {
            icon.Foreground = foreground;
        }

        if (action.Kind == TelemetryPlotRowActionKind.Toggle && action.IsChecked ||
            action.IsHighlighted)
        {
            button.BorderBrush = foreground;
            button.BorderThickness = new Thickness(1);
        }

        if (!action.IsEnabled)
        {
            button.Opacity = theme.Action.Disabled.IconOpacity;
        }
    }

    private static Color GetForegroundColor(SufniTheme theme, TelemetryPlotRowAction action)
    {
        if (!action.IsEnabled)
        {
            return theme.Action.Disabled.Text;
        }

        return action.Tone switch
        {
            TelemetryPlotRowActionTone.Accent => theme.Action.AccentPrimary,
            TelemetryPlotRowActionTone.Danger => theme.Action.Danger,
            _ when action.Kind == TelemetryPlotRowActionKind.Toggle && action.IsChecked => theme.Action.AccentPrimary,
            _ when action.IsHighlighted => theme.Action.AccentPrimary,
            _ => theme.Text.Secondary,
        };
    }
}
