using System;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.Extensibility.Views;

public partial class RecordedSessionSessionListContributionsView : UserControl
{
    private readonly Dictionary<string, ExtensionViewModelLifetime.BorrowedControl> indicatorControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExtensionViewModelLifetime.BorrowedControl> actionControls = new(StringComparer.Ordinal);
    private INotifyCollectionChanged? subscribedIndicators;
    private INotifyCollectionChanged? subscribedActions;

    public static readonly StyledProperty<IEnumerable?> IndicatorsProperty =
        AvaloniaProperty.Register<RecordedSessionSessionListContributionsView, IEnumerable?>(
            nameof(Indicators));

    public static readonly StyledProperty<IEnumerable?> ActionsProperty =
        AvaloniaProperty.Register<RecordedSessionSessionListContributionsView, IEnumerable?>(
            nameof(Actions));

    public static readonly StyledProperty<bool> ShowIndicatorsProperty =
        AvaloniaProperty.Register<RecordedSessionSessionListContributionsView, bool>(
            nameof(ShowIndicators),
            defaultValue: true);

    public static readonly StyledProperty<bool> ShowActionsProperty =
        AvaloniaProperty.Register<RecordedSessionSessionListContributionsView, bool>(
            nameof(ShowActions),
            defaultValue: true);

    public IEnumerable? Indicators
    {
        get => GetValue(IndicatorsProperty);
        set => SetValue(IndicatorsProperty, value);
    }

    public IEnumerable? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public bool ShowIndicators
    {
        get => GetValue(ShowIndicatorsProperty);
        set => SetValue(ShowIndicatorsProperty, value);
    }

    public bool ShowActions
    {
        get => GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }

    public RecordedSessionSessionListContributionsView()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IndicatorsProperty)
            {
                SubscribeToIndicators(Indicators as INotifyCollectionChanged);
                RebuildIndicators();
            }
            else if (args.Property == ActionsProperty)
            {
                SubscribeToActions(Actions as INotifyCollectionChanged);
                RebuildActions();
            }
            else if (args.Property == ShowIndicatorsProperty)
            {
                RebuildIndicators();
            }
            else if (args.Property == ShowActionsProperty)
            {
                RebuildActions();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToIndicators(Indicators as INotifyCollectionChanged);
        SubscribeToActions(Actions as INotifyCollectionChanged);
        RebuildIndicators();
        RebuildActions();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToIndicators(null);
        SubscribeToActions(null);
        ClearIndicatorControls();
        ClearActionControls();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToIndicators(INotifyCollectionChanged? collection)
    {
        if (ReferenceEquals(subscribedIndicators, collection))
        {
            return;
        }

        if (subscribedIndicators is not null)
        {
            subscribedIndicators.CollectionChanged -= OnIndicatorsChanged;
        }

        subscribedIndicators = collection;
        if (subscribedIndicators is not null)
        {
            subscribedIndicators.CollectionChanged += OnIndicatorsChanged;
        }
    }

    private void SubscribeToActions(INotifyCollectionChanged? collection)
    {
        if (ReferenceEquals(subscribedActions, collection))
        {
            return;
        }

        if (subscribedActions is not null)
        {
            subscribedActions.CollectionChanged -= OnActionsChanged;
        }

        subscribedActions = collection;
        if (subscribedActions is not null)
        {
            subscribedActions.CollectionChanged += OnActionsChanged;
        }
    }

    private void OnIndicatorsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RebuildIndicators();
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RebuildActions();
    }

    private void RebuildIndicators()
    {
        SessionListIndicatorsHost.IsVisible = ShowIndicators;
        if (!ShowIndicators || Indicators is null)
        {
            ClearIndicatorControls();
            return;
        }

        var contributions = Indicators
            .OfType<RecordedSessionListIndicatorContribution>()
            .OrderBy(static contribution => contribution.Order)
            .ToArray();
        RemoveStaleIndicatorControls(contributions);
        SessionListIndicatorsHost.Children.Clear();
        foreach (var contribution in contributions)
        {
            var borrowed = GetOrCreateIndicatorControl(contribution);
            SessionListIndicatorsHost.Children.Add(borrowed.Control);
        }
    }

    private void RebuildActions()
    {
        SessionListActionsHost.IsVisible = ShowActions;
        if (!ShowActions || Actions is null)
        {
            ClearActionControls();
            return;
        }

        var contributions = Actions
            .OfType<RecordedSessionListActionContribution>()
            .OrderBy(static contribution => contribution.Order)
            .ToArray();
        RemoveStaleActionControls(contributions);
        SessionListActionsHost.Children.Clear();
        foreach (var contribution in contributions)
        {
            var borrowed = GetOrCreateActionControl(contribution);
            SessionListActionsHost.Children.Add(borrowed.Control);
        }
    }

    private ExtensionViewModelLifetime.BorrowedControl GetOrCreateIndicatorControl(
        RecordedSessionListIndicatorContribution contribution)
    {
        var key = GetContributionKey(contribution);
        if (indicatorControls.TryGetValue(key, out var borrowed) &&
            ReferenceEquals(borrowed.ViewModel, contribution.ViewModel))
        {
            return borrowed;
        }

        borrowed = ExtensionViewModelLifetime.CreateBorrowedControl(contribution.ViewModel);
        indicatorControls[key] = borrowed;
        return borrowed;
    }

    private ExtensionViewModelLifetime.BorrowedControl GetOrCreateActionControl(
        RecordedSessionListActionContribution contribution)
    {
        var key = GetContributionKey(contribution);
        if (actionControls.TryGetValue(key, out var borrowed) &&
            ReferenceEquals(borrowed.ViewModel, contribution.ViewModel))
        {
            return borrowed;
        }

        borrowed = ExtensionViewModelLifetime.CreateBorrowedControl(contribution.ViewModel);
        actionControls[key] = borrowed;
        return borrowed;
    }

    private void RemoveStaleIndicatorControls(IReadOnlyCollection<RecordedSessionListIndicatorContribution> contributions)
    {
        var active = contributions.ToDictionary(GetContributionKey, contribution => contribution.ViewModel, StringComparer.Ordinal);
        foreach (var (key, borrowed) in indicatorControls.ToArray())
        {
            if (active.TryGetValue(key, out var viewModel) &&
                ReferenceEquals(borrowed.ViewModel, viewModel))
            {
                continue;
            }

            indicatorControls.Remove(key);
        }
    }

    private void RemoveStaleActionControls(IReadOnlyCollection<RecordedSessionListActionContribution> contributions)
    {
        var active = contributions.ToDictionary(GetContributionKey, contribution => contribution.ViewModel, StringComparer.Ordinal);
        foreach (var (key, borrowed) in actionControls.ToArray())
        {
            if (active.TryGetValue(key, out var viewModel) &&
                ReferenceEquals(borrowed.ViewModel, viewModel))
            {
                continue;
            }

            actionControls.Remove(key);
        }
    }

    private void ClearIndicatorControls()
    {
        indicatorControls.Clear();
        SessionListIndicatorsHost.Children.Clear();
    }

    private void ClearActionControls()
    {
        actionControls.Clear();
        SessionListActionsHost.Children.Clear();
    }

    private static string GetContributionKey(RecordedSessionListIndicatorContribution contribution) =>
        $"{contribution.ExtensionId}\u001f{contribution.ContributionId}";

    private static string GetContributionKey(RecordedSessionListActionContribution contribution) =>
        $"{contribution.ExtensionId}\u001f{contribution.ContributionId}";
}
