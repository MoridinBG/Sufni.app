using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;

using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.LiveDaq.Views.Editors;
using Sufni.App.Acquisition.Services;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.Views.Shared;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.LiveDaq.Views.Editors;

[Collection("Ui")]
public class LiveDaqDetailViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqDetailView_BackAndStartSessionButtons_ShareBottomActionRow()
    {
        var editor = CreateEditor();
        await using var mounted = await MountAsync(editor);

        var backButton = mounted.View.FindControl<Button>("BackButton")!;
        var startSessionButton = mounted.View.FindControl<Button>("StartSessionButton")!;

        Assert.Same(backButton.Parent, startSessionButton.Parent);
        Assert.Equal(0, Grid.GetColumn(backButton));
        Assert.Equal(1, Grid.GetColumn(startSessionButton));
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_ErrorMessagesBar_IsFixedAboveBottomActions()
    {
        var editor = CreateEditor();
        await using var mounted = await MountAsync(editor);

        var errorBar = mounted.View.FindControl<ErrorMessagesBar>("ManagementErrorMessagesBar")!;
        var scrollViewer = mounted.View.FindControl<ScrollViewer>("DiagnosticsScrollViewer")!;

        Assert.NotSame(scrollViewer, errorBar.Parent);
        Assert.Equal(1, Grid.GetRow(errorBar));
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_DeviceManagementCard_UsesMobilePadding()
    {
        var editor = CreateEditor();
        await using var mounted = await MountAsync(editor);

        var card = mounted.View.FindControl<LiveDaqDeviceManagementCard>("DeviceManagementCard")!;

        Assert.Equal(new Thickness(14), card.CardPadding);
    }

    private static LiveDaqDetailViewModel CreateEditor()
    {
        var editor = CreateEditorWithManagement();
        editor.Name = "Board 1";
        editor.CanConnect = true;
        editor.CanDisconnect = false;
        editor.CanStartSession = false;
        editor.AreRequestedRatesEnabled = true;
        editor.Snapshot = LiveDaqUiSnapshot.Empty;
        return editor;
    }

    private static LiveDaqDetailViewModel CreateEditorWithManagement()
    {
        var sharedStream = Substitute.For<ILiveDaqSharedStream>();
        sharedStream.Frames.Returns(new Subject<LiveProtocolFrame>());
        sharedStream.States.Returns(new BehaviorSubject<LiveDaqSharedStreamState>(LiveDaqSharedStreamState.Empty));
        sharedStream.CurrentState.Returns(LiveDaqSharedStreamState.Empty);
        sharedStream.RequestedConfiguration.Returns(LiveDaqStreamConfiguration.Default);

        var knownBoardsQuery = Substitute.For<ILiveDaqKnownBoardsQuery>();
        knownBoardsQuery.Changes.Returns(new BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>>([]));

        return new LiveDaqDetailViewModel(
            new LiveDaqSnapshot(
                IdentityKey: "board-1",
                DisplayName: "Board 1",
                BoardId: "board-1",
                Host: "192.168.0.50",
                Port: 1557,
                IsOnline: true,
                SetupName: "race",
                BikeName: "demo"),
            sharedStream,
            TestCoordinatorSubstitutes.LiveDaq(),
            Substitute.For<IDaqManagementService>(),
            Substitute.For<IFilesService>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>(),
            knownBoardsQuery,
            new LiveDaqStore(),
            new InlineUiThreadDispatcher())
        {
            Snapshot = LiveDaqUiSnapshot.Empty
        };
    }

    private static async Task<MountedLiveDaqDetailView> MountAsync(LiveDaqDetailViewModel editor)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var view = new LiveDaqDetailView
        {
            DataContext = editor
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedLiveDaqDetailView(host, view);
    }
}

internal sealed class MountedLiveDaqDetailView(Window host, LiveDaqDetailView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqDetailView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
