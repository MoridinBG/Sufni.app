using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Views;
using Sufni.App.Sessions.Media.DesktopViews.Items;
using Sufni.App.Sessions.Media.Views.Controls;
using Sufni.App.Shared.DesktopViews.Controls;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;

namespace Sufni.App.Tests.Sessions.Media.DesktopViews.Items;

[Collection("Ui")]
public class SessionMediaDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionMediaDesktopView_ComposesMapMediaAndExtensionPanes()
    {
        var workspace = CreateWorkspace(mediaUrl: "media.mp4");
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(workspace);

        Assert.True(mounted.View.FindControl<Grid>("MediaContentRoot")!.IsVisible);
        Assert.True(mounted.View.FindControl<PlaceholderOverlayContainer>("MediaHost")!.IsVisible);
        Assert.True(mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost")!.IsVisible);
        Assert.Single(mounted.View.GetVisualDescendants().OfType<MapView>());
        Assert.True(mounted.View.FindControl<RecordedSessionMediaPanesView>("MediaPanesHost")!.IsVisible);
        AssertContributionText(mounted.View, "Media pane");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_PrimarySplitCommit_DoesNotPersistSyntheticPaneId()
    {
        var workspace = CreateWorkspace(mediaUrl: "media.mp4");
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.2),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.3),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.5),
            ]));

        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        primarySplit.BeginDragForTests();
        primarySplit.DragToFirstRatioForTests(0.4);
        primarySplit.CompleteDragForTests();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(mounted.View.LayoutPreferences);
        Assert.Equal(
            [SessionLayoutPaneIds.Media, SessionLayoutPaneIds.Map, SessionLayoutPaneIds.ExtensionMedia],
            mounted.View.LayoutPreferences!.Panes.Select(pane => pane.PaneId).ToArray());
        Assert.Equal(0.4, mounted.View.LayoutPreferences.Panes[0].Ratio, precision: 6);
        Assert.Equal(0.225, mounted.View.LayoutPreferences.Panes[1].Ratio, precision: 6);
        Assert.Equal(0.375, mounted.View.LayoutPreferences.Panes[2].Ratio, precision: 6);
    }

    private static async Task<MountedSessionMediaDesktopView> MountAsync(
        TestSessionMediaWorkspace workspace,
        SessionPaneGroupPreferences? layoutPreferences = null)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new SessionMediaDesktopView
        {
            DataContext = workspace,
            LayoutPreferences = layoutPreferences,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionMediaDesktopView(host, view);
    }

    private static TestSessionMediaWorkspace CreateWorkspace(string? mediaUrl)
    {
        return new TestSessionMediaWorkspace(
            trackPoints:
            [
                new TrackPoint(1, 2, 3, 4),
            ],
            mediaUrl: mediaUrl);
    }

    private static RecordedSessionMediaPaneContribution CreateMediaPaneContribution(string text)
    {
        return new RecordedSessionMediaPaneContribution(
            "extension",
            "media-pane",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "DesktopMediaPane", Text = text },
            });
    }

    private static void AssertContributionText(Control root, string text)
    {
        var textBlocks = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToArray();
        var textBlock = textBlocks.SingleOrDefault(textBlock => textBlock.Text == text);
        Assert.True(
            textBlock is not null,
            $"Expected contribution text '{text}'. Actual text blocks: {string.Join(", ", textBlocks.Select(block => $"{block.Name}:{block.Text}"))}");
        Assert.Equal(text, textBlock!.Text);
    }

    private static CollapsibleSplitView FindSplit(SessionMediaDesktopView view, string name)
    {
        return view.GetVisualDescendants()
            .OfType<CollapsibleSplitView>()
            .Single(split => split.Name == name);
    }
}

internal sealed class MountedSessionMediaDesktopView(Window host, SessionMediaDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionMediaDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
