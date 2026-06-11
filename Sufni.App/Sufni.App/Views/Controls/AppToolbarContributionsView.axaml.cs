using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts;

namespace Sufni.App.Views.Controls;

public partial class AppToolbarContributionsView : UserControl
{
    private INotifyCollectionChanged? subscribedContributions;

    public static readonly StyledProperty<IEnumerable?> ContributionsProperty =
        AvaloniaProperty.Register<AppToolbarContributionsView, IEnumerable?>(
            nameof(Contributions));

    public IEnumerable? Contributions
    {
        get => GetValue(ContributionsProperty);
        set => SetValue(ContributionsProperty, value);
    }

    public AppToolbarContributionsView()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == ContributionsProperty)
            {
                SubscribeToContributions(Contributions);
                Rebuild();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToContributions(Contributions);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToContributions(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToContributions(IEnumerable? contributions)
    {
        if (ReferenceEquals(subscribedContributions, contributions))
        {
            return;
        }

        if (subscribedContributions is not null)
        {
            subscribedContributions.CollectionChanged -= OnContributionsChanged;
        }

        subscribedContributions = contributions as INotifyCollectionChanged;
        if (subscribedContributions is not null)
        {
            subscribedContributions.CollectionChanged += OnContributionsChanged;
        }
    }

    private void OnContributionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        AppToolbarContributionsHost.Children.Clear();
        if (Contributions is null)
        {
            return;
        }

        foreach (var contribution in Contributions
                     .OfType<AppToolbarContribution>()
                     .OrderBy(static contribution => contribution.Order))
        {
            AppToolbarContributionsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private static Control CreateContributionControl(IExtensionViewModel viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }
}
