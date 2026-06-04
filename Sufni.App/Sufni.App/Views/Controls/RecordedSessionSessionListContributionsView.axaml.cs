using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.RecordedSessions;

namespace Sufni.App.Views.Controls;

public partial class RecordedSessionSessionListContributionsView : UserControl
{
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
        SessionListIndicatorsHost.Children.Clear();
        SessionListIndicatorsHost.IsVisible = ShowIndicators;
        if (!ShowIndicators || Indicators is null)
        {
            return;
        }

        foreach (var contribution in Indicators
                     .OfType<RecordedSessionListIndicatorContribution>()
                     .OrderBy(static contribution => contribution.Order))
        {
            SessionListIndicatorsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private void RebuildActions()
    {
        SessionListActionsHost.Children.Clear();
        SessionListActionsHost.IsVisible = ShowActions;
        if (!ShowActions || Actions is null)
        {
            return;
        }

        foreach (var contribution in Actions
                     .OfType<RecordedSessionListActionContribution>()
                     .OrderBy(static contribution => contribution.Order))
        {
            SessionListActionsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private static Control CreateContributionControl(object viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }
}
