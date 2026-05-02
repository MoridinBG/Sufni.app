using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.SessionPages;

namespace Sufni.App.Tests.Views.Items;

public class SessionSidebarDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionSidebarDesktopView_RendersNotesAndPreferencesTabs()
    {
        var workspace = new SessionSidebarWorkspaceStub
        {
            Name = "Recorded Session 01",
            DescriptionText = "Suspension notes",
        };
        workspace.PreferencesPage.ApplyPlotPreferences(new SessionPlotPreferences(Travel: false, Velocity: true, Imu: true));
        workspace.PreferencesPage.ApplyPlotAvailability(travelAvailable: true, velocityAvailable: false, imuAvailable: true);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabControl>("SidebarTabControl");
        var notesTab = mounted.View.FindControl<TabItem>("NotesTab");
        var preferencesTab = mounted.View.FindControl<TabItem>("PreferencesTab");

        Assert.NotNull(tabControl);
        Assert.NotNull(notesTab);
        Assert.NotNull(preferencesTab);
        Assert.Equal("Notes", notesTab!.Header);
        Assert.Equal("Preferences", preferencesTab!.Header);
        Assert.NotNull(mounted.View.FindControl<TextBox>("NameTextBox"));
        Assert.NotNull(mounted.View.FindControl<TextBox>("DescriptionTextBox"));

        preferencesTab!.IsSelected = true;
        tabControl!.SelectedItem = preferencesTab;
        tabControl.SelectedIndex = 1;
        await ViewTestHelpers.FlushDispatcherAsync();

        var preferencesView = FindVisual<PreferencesPageView>(mounted.View, "PreferencesContent");
        Assert.Same(workspace.PreferencesPage, preferencesView.DataContext);
    }

    [AvaloniaFact]
    public async Task SessionSidebarDesktopView_BindsNameAndDescriptionFields()
    {
        var workspace = new SessionSidebarWorkspaceStub
        {
            Name = "Recorded Session 01",
            DescriptionText = "Suspension notes",
        };

        await using var mounted = await MountAsync(workspace);

        var nameTextBox = mounted.View.FindControl<TextBox>("NameTextBox");
        var descriptionTextBox = mounted.View.FindControl<TextBox>("DescriptionTextBox");

        Assert.NotNull(nameTextBox);
        Assert.NotNull(descriptionTextBox);
        Assert.Equal("Recorded Session 01", nameTextBox!.Text);
        Assert.Equal("Suspension notes", descriptionTextBox!.Text);

        workspace.Name = "Renamed Session";
        workspace.DescriptionText = "Updated notes";
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("Renamed Session", nameTextBox.Text);
        Assert.Equal("Updated notes", descriptionTextBox.Text);
    }

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

    private static async Task<MountedSessionSidebarDesktopView> MountAsync(SessionSidebarWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new SessionSidebarDesktopView
        {
            DataContext = workspace,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionSidebarDesktopView(host, view);
    }

    private static T FindVisual<T>(Control root, string name)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .Concat(root.GetLogicalDescendants().OfType<T>())
            .Single(control => control.Name == name);
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
        public PreferencesPageViewModel PreferencesPage { get; } = new();
        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand ResetCommand { get; }
        public bool SaveInvoked { get; private set; }
        public bool ResetInvoked { get; private set; }
    }
}

internal sealed class MountedSessionSidebarDesktopView(Window host, SessionSidebarDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionSidebarDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}