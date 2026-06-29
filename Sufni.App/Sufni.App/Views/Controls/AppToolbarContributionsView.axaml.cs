using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using Sufni.App.ExtensionHost.Contracts;

namespace Sufni.App.Views.Controls;

public partial class AppToolbarContributionsView : UserControl
{
    private static readonly Uri SvgAssetBaseUri =
        new($"avares://{typeof(global::Sufni.App.App).Assembly.GetName().Name}/");

    private INotifyCollectionChanged? subscribedCommandContributions;
    private INotifyCollectionChanged? subscribedViewContributions;

    public static readonly StyledProperty<IEnumerable?> CommandContributionsProperty =
        AvaloniaProperty.Register<AppToolbarContributionsView, IEnumerable?>(
            nameof(CommandContributions));

    public static readonly StyledProperty<IEnumerable?> ViewContributionsProperty =
        AvaloniaProperty.Register<AppToolbarContributionsView, IEnumerable?>(
            nameof(ViewContributions));

    public IEnumerable? CommandContributions
    {
        get => GetValue(CommandContributionsProperty);
        set => SetValue(CommandContributionsProperty, value);
    }

    public IEnumerable? ViewContributions
    {
        get => GetValue(ViewContributionsProperty);
        set => SetValue(ViewContributionsProperty, value);
    }

    public AppToolbarContributionsView()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == CommandContributionsProperty)
            {
                SubscribeToCommandContributions(CommandContributions);
                Rebuild();
            }

            if (args.Property == ViewContributionsProperty)
            {
                SubscribeToViewContributions(ViewContributions);
                Rebuild();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToCommandContributions(CommandContributions);
        SubscribeToViewContributions(ViewContributions);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToCommandContributions(null);
        SubscribeToViewContributions(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToCommandContributions(IEnumerable? contributions)
    {
        if (ReferenceEquals(subscribedCommandContributions, contributions))
        {
            return;
        }

        if (subscribedCommandContributions is not null)
        {
            subscribedCommandContributions.CollectionChanged -= OnContributionsChanged;
        }

        subscribedCommandContributions = contributions as INotifyCollectionChanged;
        if (subscribedCommandContributions is not null)
        {
            subscribedCommandContributions.CollectionChanged += OnContributionsChanged;
        }
    }

    private void SubscribeToViewContributions(IEnumerable? contributions)
    {
        if (ReferenceEquals(subscribedViewContributions, contributions))
        {
            return;
        }

        if (subscribedViewContributions is not null)
        {
            subscribedViewContributions.CollectionChanged -= OnContributionsChanged;
        }

        subscribedViewContributions = contributions as INotifyCollectionChanged;
        if (subscribedViewContributions is not null)
        {
            subscribedViewContributions.CollectionChanged += OnContributionsChanged;
        }
    }

    private void OnContributionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        AppToolbarCommandBar.PrimaryCommands.Clear();
        AppToolbarViewContributionsHost.Children.Clear();

        foreach (var contribution in OrderedCommandContributions())
        {
            AppToolbarCommandBar.PrimaryCommands.Add(CreateCommandBarButton(contribution));
        }

        foreach (var contribution in OrderedViewContributions())
        {
            AppToolbarViewContributionsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private IOrderedEnumerable<AppToolbarCommandContribution> OrderedCommandContributions()
    {
        return (CommandContributions ?? Enumerable.Empty<object>())
            .OfType<AppToolbarCommandContribution>()
            .OrderBy(static contribution => contribution.Order)
            .ThenBy(static contribution => contribution.ExtensionId, System.StringComparer.Ordinal)
            .ThenBy(static contribution => contribution.ContributionId, System.StringComparer.Ordinal);
    }

    private IOrderedEnumerable<AppToolbarViewContribution> OrderedViewContributions()
    {
        return (ViewContributions ?? Enumerable.Empty<object>())
            .OfType<AppToolbarViewContribution>()
            .OrderBy(static contribution => contribution.Order)
            .ThenBy(static contribution => contribution.ExtensionId, System.StringComparer.Ordinal)
            .ThenBy(static contribution => contribution.ContributionId, System.StringComparer.Ordinal);
    }

    private static CommandBarButton CreateCommandBarButton(AppToolbarCommandContribution contribution)
    {
        return new CommandBarButton
        {
            Label = contribution.Label,
            Icon = CreateIcon(contribution.Icon),
            Command = contribution.Command,
            CommandParameter = contribution.CommandParameter,
        };
    }

    private static Control CreateContributionControl(IExtensionViewModel viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }

    private static Image? CreateIcon(ToolbarIconDescriptor? descriptor)
    {
        return descriptor is null
            ? null
            : new Image
            {
                Width = descriptor.Width,
                Height = descriptor.Height,
                Source = new SvgImage { Source = SvgSource.Load(descriptor.AssetPath, SvgAssetBaseUri) },
            };
    }
}
