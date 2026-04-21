using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.Tests.Views.Items;

public class SessionSidebarDesktopViewTests
{
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