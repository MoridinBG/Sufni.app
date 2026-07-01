using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
namespace Sufni.App.Extensibility.Views;

public partial class RecordedSessionStatisticsContributionsView : UserControl
{
    private RecordedSessionExtensionSlots? subscribedSlots;

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<RecordedSessionStatisticsContributionsView, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public RecordedSessionStatisticsContributionsView()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == ExtensionSlotsProperty)
            {
                SubscribeToSlots(ExtensionSlots);
                Rebuild();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToSlots(ExtensionSlots);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToSlots(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToSlots(RecordedSessionExtensionSlots? slots)
    {
        if (ReferenceEquals(subscribedSlots, slots))
        {
            return;
        }

        if (subscribedSlots is not null)
        {
            subscribedSlots.StatisticsBanners.CollectionChanged -= OnStatisticsBannersChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.StatisticsBanners.CollectionChanged += OnStatisticsBannersChanged;
        }
    }

    private void OnStatisticsBannersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        RecordedSessionStatisticsBannersHost.Children.Clear();
        if (ExtensionSlots is not { } slots)
        {
            return;
        }

        foreach (var contribution in slots.StatisticsBanners.OrderBy(static contribution => contribution.Order))
        {
            RecordedSessionStatisticsBannersHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private static Control CreateContributionControl(IExtensionViewModel viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }
}
