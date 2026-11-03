using Sufni.App.Shared.Models;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using System.Reactive.Subjects;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shell.Views;

[Collection("Ui")]
public class ShellStartupViewTests
{
    [AvaloniaTheory]
    [InlineData(UiLayoutProfile.Compact)]
    [InlineData(UiLayoutProfile.Workspace)]
    public async Task Failure_BlocksPages_AndResetLeavesOnlyQuit(UiLayoutProfile profile)
    {
        ViewTestHelpers.EnsureViewTestResources();
        using var states = new BehaviorSubject<ShellStartupState>(new ShellStartupState.Starting());
        var startup = Substitute.For<IAppStartup>();
        startup.Observe().Returns(states);
        startup.LogPath.Returns("launch.log");
        startup.ResetDatabaseAsync().Returns(_ =>
        {
            states.OnNext(new ShellStartupState.RelaunchRequired("archive"));
            return Task.FromResult<DatabaseResetResult>(new DatabaseResetResult.Reset("archive"));
        });
        using var root = new ShellRootViewModel(startup, Substitute.For<IAppShutdown>(), TestShellFeedback.Create(),
            new ShellWorkspaceViewModel(new InlineUiThreadDispatcher(), new FakeTimeProvider()),
            TestAppEnvironment.Create(profile), Substitute.For<IPlotZoomState>(),
            new InlineUiThreadDispatcher(), new FakeTimeProvider());
        UserControl view = profile == UiLayoutProfile.Compact ? new CompactShellView() : new WorkspaceShellView();
        view.DataContext = root;
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            states.OnNext(new ShellStartupState.Failed(StartupFailureReason.Database, "diagnostic details"));
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.False(view.FindControl<Panel>("ReadyContent")!.IsVisible);
            var failure = view.FindControl<StartupView>("StartupSurface")!;
            Assert.True(failure.IsVisible);
            var reset = failure.FindControl<Button>("ResetDatabase")!;
            Assert.True(reset.IsVisible);
            Assert.True(reset.Command!.CanExecute(null));
            reset.Command.Execute(reset.CommandParameter);
            await (root.ResetDatabaseCommand.ExecutionTask ?? Task.CompletedTask).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.False(reset.IsVisible);
            Assert.False(view.FindControl<Panel>("ReadyContent")!.IsVisible);
            Assert.True(failure.FindControl<Button>("Quit")!.IsVisible);
            Assert.Null(root.Pages);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
