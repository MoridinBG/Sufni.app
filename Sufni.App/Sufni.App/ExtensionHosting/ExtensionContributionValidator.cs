using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHosting;
using Sufni.App.ExtensionHost.Contracts;

namespace Sufni.App.ExtensionHosting;

internal static class ExtensionContributionValidator
{
    public static void ValidateAppToolbarContribution(
        IExtensionContribution contribution,
        string ownerExtensionId,
        ContributionIdTracker? contributionIds = null)
    {
        ValidateContribution(contribution, ownerExtensionId, "app toolbar");
        contributionIds?.Add(contribution, "app toolbar");
    }

    public static void ValidateRecordedSessionListContributions(
        string ownerExtensionId,
        IEnumerable<RecordedSessionListIndicatorContribution> indicators,
        IEnumerable<RecordedSessionListActionContribution> actions)
    {
        ValidateRequiredId(ownerExtensionId, "Recorded-session list contribution provider");
        var contributionIds = new ContributionIdTracker("recorded-session list contributions");
        ValidateContributions(
            indicators,
            ownerExtensionId,
            "recorded-session list indicators",
            contributionIds);
        ValidateContributions(
            actions,
            ownerExtensionId,
            "recorded-session list actions",
            contributionIds);
    }

    public static void ValidateRecordedSessionExtensionFactories(
        IReadOnlyList<IRecordedSessionExtensionFactory> factories)
    {
        var extensionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ValidateRequiredId(factory.ExtensionId, "Recorded-session extension factory");
            if (!extensionIds.Add(factory.ExtensionId))
            {
                throw new InvalidOperationException(
                    $"More than one recorded-session extension factory is registered for extension '{factory.ExtensionId}'.");
            }
        }
    }

    public static void ValidateRecordedSessionSlots(
        RecordedSessionExtensionSlots slots,
        string ownerExtensionId)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ValidateRequiredId(ownerExtensionId, "Recorded-session extension owner");

        var contributionIds = new ContributionIdTracker("recorded-session slots");
        var hostedRowTargets = ValidateHostedRows(slots.HostedGraphRows, ownerExtensionId, contributionIds);
        ValidateContributions(slots.GraphToolbarCommands, ownerExtensionId, "recorded-session graph toolbar commands", contributionIds);
        ValidateContributions(slots.GraphToolbarViews, ownerExtensionId, "recorded-session graph toolbar views", contributionIds);
        ValidateContributions(slots.Pages, ownerExtensionId, "recorded-session pages", contributionIds);
        ValidateContributions(slots.MediaPanes, ownerExtensionId, "recorded-session media panes", contributionIds);
        ValidateContributions(slots.MapOverlays, ownerExtensionId, "recorded-session map overlays", contributionIds);
        ValidateContributions(slots.StatisticsBanners, ownerExtensionId, "recorded-session statistics banners", contributionIds);
        ValidateContributions(slots.StatisticsTabs, ownerExtensionId, "recorded-session statistics tabs", contributionIds);
        ValidateContributions(slots.StatisticsOverlays, ownerExtensionId, "recorded-session statistics overlays", contributionIds);
        ValidateContributions(slots.StatisticsMetrics, ownerExtensionId, "recorded-session statistics metrics", contributionIds);
        ValidateContributions(slots.SessionListIndicators, ownerExtensionId, "recorded-session list indicators", contributionIds);
        ValidateContributions(slots.SessionListActions, ownerExtensionId, "recorded-session list actions", contributionIds);
        ValidateContributions(slots.PlotContextMenuActions, ownerExtensionId, "recorded-session plot context menu actions", contributionIds);
        ValidateContributions(slots.PlotRowHeaderActions, ownerExtensionId, "recorded-session plot row actions", contributionIds);
        ValidateContributions(slots.TimeRangeOverlays, ownerExtensionId, "recorded-session time-range overlays", contributionIds);

        foreach (var contribution in slots.PlotContextMenuActions)
        {
            ValidateBuiltInRow(contribution.TargetRow, "recorded-session plot context menu action target");
        }

        foreach (var contribution in slots.PlotRowHeaderActions)
        {
            ValidateRowTargetReference(
                contribution.TargetRow,
                ownerExtensionId,
                hostedRowTargets,
                "recorded-session plot row action target");
        }

        foreach (var contribution in slots.TimeRangeOverlays)
        {
            ValidateRowTargetReference(
                contribution.TargetRow,
                ownerExtensionId,
                hostedRowTargets,
                "recorded-session time-range overlay target");
        }
    }

    public static void ValidateRequiredId(string id, string owner)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"{owner} id is required.");
        }
    }

    private static IReadOnlySet<string> ValidateHostedRows(
        IEnumerable<RecordedSessionHostedGraphRowContribution> contributions,
        string ownerExtensionId,
        ContributionIdTracker contributionIds)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contribution in contributions)
        {
            ValidateContribution(contribution, ownerExtensionId, "recorded-session hosted graph row");
            contributionIds.Add(contribution, "recorded-session hosted graph row");
            ValidateBuiltInRow(contribution.ParentRow, "recorded-session hosted graph row parent");
            if (contribution.RowTarget.IsBuiltIn ||
                !StringComparer.Ordinal.Equals(contribution.RowTarget.ExtensionId, contribution.ExtensionId) ||
                !StringComparer.Ordinal.Equals(contribution.RowTarget.ContributionId, contribution.ContributionId))
            {
                throw new InvalidOperationException(
                    $"Recorded-session hosted graph row contribution '{contribution.ExtensionId}:{contribution.ContributionId}' must use a row target matching its extension id and contribution id.");
            }

            if (!targets.Add(contribution.RowTarget.StableKey))
            {
                throw new InvalidOperationException(
                    $"Duplicate recorded-session hosted graph row target '{contribution.RowTarget.StableKey}'.");
            }
        }

        return targets;
    }

    private static void ValidateContributions<T>(
        IEnumerable<T> contributions,
        string ownerExtensionId,
        string surface,
        ContributionIdTracker? contributionIds = null)
        where T : IExtensionContribution
    {
        foreach (var contribution in contributions)
        {
            ValidateContribution(contribution, ownerExtensionId, surface);
            contributionIds?.Add(contribution, surface);
        }
    }

    private static void ValidateContribution(
        IExtensionContribution contribution,
        string ownerExtensionId,
        string surface)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ValidateRequiredId(ownerExtensionId, $"{surface} owner");
        ValidateRequiredId(contribution.ExtensionId, $"{surface} contribution extension");
        ValidateRequiredId(contribution.ContributionId, $"{surface} contribution");
        if (!StringComparer.Ordinal.Equals(contribution.ExtensionId, ownerExtensionId))
        {
            throw new InvalidOperationException(
                $"Extension contribution '{contribution.ExtensionId}:{contribution.ContributionId}' on surface '{surface}' does not match owning extension '{ownerExtensionId}'.");
        }
    }

    private static void ValidateRowTargetReference(
        RecordedSessionGraphRowTarget target,
        string ownerExtensionId,
        IReadOnlySet<string> hostedRowTargets,
        string targetName)
    {
        if (target.IsBuiltIn)
        {
            ValidateBuiltInRow(target.BuiltInRow!.Value, targetName);
            return;
        }

        ValidateRequiredId(target.ExtensionId ?? "", $"{targetName} extension");
        ValidateRequiredId(target.ContributionId ?? "", $"{targetName} contribution");
        if (!StringComparer.Ordinal.Equals(target.ExtensionId, ownerExtensionId))
        {
            throw new InvalidOperationException(
                $"{targetName} '{target.StableKey}' must target the owning extension '{ownerExtensionId}'.");
        }

        if (!hostedRowTargets.Contains(target.StableKey))
        {
            throw new InvalidOperationException(
                $"{targetName} '{target.StableKey}' does not refer to a hosted graph row published by extension '{ownerExtensionId}'.");
        }
    }

    private static void ValidateBuiltInRow(RecordedSessionBuiltInGraphRow row, string targetName)
    {
        if (!Enum.IsDefined(row))
        {
            throw new InvalidOperationException($"{targetName} has invalid built-in row value '{row}'.");
        }
    }

    public sealed class ContributionIdTracker(string scope)
    {
        private readonly Dictionary<ContributionKey, string> surfacesByContribution = [];

        public void Add(IExtensionContribution contribution, string surface)
        {
            var key = new ContributionKey(contribution.ExtensionId, contribution.ContributionId);
            if (surfacesByContribution.TryGetValue(key, out var existingSurface))
            {
                throw new InvalidOperationException(
                    $"Duplicate extension contribution id '{contribution.ContributionId}' for extension '{contribution.ExtensionId}' in {scope}. First surface: '{existingSurface}'. Duplicate surface: '{surface}'.");
            }

            surfacesByContribution.Add(key, surface);
        }
    }

    private readonly record struct ContributionKey(string ExtensionId, string ContributionId);
}
