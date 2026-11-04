using Sufni.App.Sessions.Presentation;
using Sufni.App.Tests.TestSupport.Fixtures;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Sessions.Detail.DesktopViews.Items;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Pages.Views.SessionPages;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Detail.DesktopViews.Items;

[Collection("Ui")]
public class SessionSidebarDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionSidebarDesktopView_WiresSaveAndResetCommands()
    {
        var workspace = new SessionSidebarWorkspaceStub
        {
            Name = "Recorded Session 01",
            DescriptionText = "Suspension notes",
        };

        await using var mounted = await MountAsync(workspace);

        var saveButton = mounted.View.FindControl<Button>("SaveButton");
        var resetButton = mounted.View.FindControl<Button>("ResetButton");

        Assert.NotNull(saveButton);
        Assert.NotNull(resetButton);
        Assert.Same(workspace.SaveCommand, saveButton!.Command);
        Assert.Same(workspace.ResetCommand, resetButton!.Command);
        Assert.True(saveButton.Command!.CanExecute(saveButton.CommandParameter));
        Assert.True(resetButton.Command!.CanExecute(resetButton.CommandParameter));

        saveButton.Command.Execute(saveButton.CommandParameter);
        resetButton.Command.Execute(resetButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(workspace.SaveInvoked);
        Assert.True(workspace.ResetInvoked);
    }

    [AvaloniaFact]
    public async Task SessionSidebarDesktopView_ShowsSaveAndResetOnlyInNotesTab()
    {
        var workspace = new SessionSidebarWorkspaceStub
        {
            Name = "Recorded Session 01",
            DescriptionText = "Suspension notes",
        };

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabControl>("SidebarTabControl");
        var saveButton = mounted.View.FindControl<Button>("SaveButton");
        var resetButton = mounted.View.FindControl<Button>("ResetButton");

        Assert.True(saveButton!.IsVisible);
        Assert.True(resetButton!.IsVisible);

        var preferencesTab = mounted.View.FindControl<TabItem>("PreferencesTab");
        preferencesTab!.IsSelected = true;
        tabControl!.SelectedItem = preferencesTab;
        tabControl.SelectedIndex = 1;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(FindVisibleButtons(mounted.View, "SaveButton"));
        Assert.Empty(FindVisibleButtons(mounted.View, "ResetButton"));
    }

    private static async Task<MountedView<SessionSidebarDesktopView>> MountAsync(SessionSidebarWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new SessionSidebarDesktopView
        {
            DataContext = workspace,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedView<SessionSidebarDesktopView>(host, view);
    }

    private static IEnumerable<Button> FindVisibleButtons(Control root, string name)
    {
        return root.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Name == name && button.IsVisible)
            .ToArray();
    }

    private sealed class SessionSidebarWorkspaceStub : ObservableObject, ISessionSidebarWorkspace
    {
        private string? name;
        private string? descriptionText;

        public SessionSidebarWorkspaceStub()
        {
            SaveCommand = new AsyncRelayCommand(() =>
            {
                SaveInvoked = true;
                return Task.CompletedTask;
            });
            ResetCommand = new AsyncRelayCommand(() =>
            {
                ResetInvoked = true;
                return Task.CompletedTask;
            });
        }

        public string? Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }

        public string? DescriptionText
        {
            get => descriptionText;
            set => SetProperty(ref descriptionText, value);
        }

        public SuspensionSettings ForkSettings { get; } = new();
        public SuspensionSettings ShockSettings { get; } = new();
        public NotesPageViewModel NotesPage { get; } = new();
        public PreferencesPageViewModel PreferencesPage { get; } = TestPreferencesPages.Create();
        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand ResetCommand { get; }
        public bool SaveInvoked { get; private set; }
        public bool ResetInvoked { get; private set; }
    }
}
