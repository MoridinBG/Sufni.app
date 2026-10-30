using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shell.Views;

[Collection("Ui")]
public class ShellFeedbackViewTests
{
    [AvaloniaTheory]
    [InlineData(UiLayoutProfile.Compact)]
    [InlineData(UiLayoutProfile.Workspace)]
    public async Task SyncFeedback_RemainsVisibleWhenThePrimaryPageChanges(UiLayoutProfile profile)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(profile);
        var environment = TestAppEnvironment.Create(profile);
        var sync = TestCoordinatorSubstitutes.Sync();
        var pages = MainPagesViewModelTestFactory.Create(syncCoordinator: sync, appEnvironment: environment);
        using var root = ShellRootViewModelTestFactory.Create(pages,
            new ShellWorkspaceViewModel(new InlineUiThreadDispatcher(), new FakeTimeProvider()), environment,
            Substitute.For<IPlotZoomState>(), new InlineUiThreadDispatcher(), new FakeTimeProvider());
        UserControl view = profile == UiLayoutProfile.Compact ? new CompactShellView() : new WorkspaceShellView();
        view.DataContext = root;
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            sync.SyncCompleted += Raise.Event<EventHandler<SyncCompletedEventArgs>>(sync, new SyncCompletedEventArgs("Synced"));
            pages.SelectedPrimaryIndex = 2;
            await ViewTestHelpers.FlushDispatcherAsync();

            var feedback = view.FindControl<ShellFeedbackBar>("ShellFeedback")!;
            var notifications = feedback.FindControl<NotificationsBar>("Notifications")!;
            Assert.True(notifications.IsVisible);
            Assert.Single(notifications.FindControl<ListBox>("NotificationsListBox")!.Items);
            Assert.Empty(pages.SessionsPage.Notifications);
            Assert.Empty(pages.BikesPage.Notifications);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
