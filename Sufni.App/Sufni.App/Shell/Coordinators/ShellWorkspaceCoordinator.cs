using Sufni.App.Shell.ViewModels.Base;
using Sufni.App.Shell.Services.Preferences;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.LiveDaq.ViewModels.Editors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.Coordinators;

public sealed class ShellWorkspaceCoordinator(
    ShellWorkspaceViewModel workspace,
    IAppEnvironment appEnvironment,
    IEditorFactory editorFactory,
    IUiPreferences uiPreferences,
    IUiThreadDispatcher dispatcher,
    ShellFeedbackViewModel feedback) : IShellCoordinator, IAsyncDisposable
{
    private static readonly ILogger logger = Log.ForContext<ShellWorkspaceCoordinator>();
    private readonly System.Threading.Lock cleanupGate = new();
    private readonly HashSet<Task> detachedTabCleanup = [];
    private bool disposed;
    private bool ShouldRememberClosedTabs => appEnvironment.LayoutProfile == UiLayoutProfile.Workspace;

    public async Task SwitchLayoutProfileAsync(UiLayoutProfile profile)
    {
        await uiPreferences.SetLayoutProfileAsync(profile);
        await dispatcher.InvokeAsync(() => appEnvironment.SetLayoutProfile(profile));
    }

    public void OpenEditor(EditorKey key, bool background = false)
    {
        var existing = workspace.Tabs.FirstOrDefault(tab => Matches(key, tab));
        if (existing is LiveDaqDetailViewModel { IsStreamClosed: true } closed)
        {
            lock (cleanupGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                detachedTabCleanup.RemoveWhere(task => task.IsCompleted);
                detachedTabCleanup.Add(CloseDetachedTabAsync(closed));
            }
            workspace.CloseTab(closed, rememberForRestore: false);
            existing = null;
        }
        if (existing is not null)
        {
            if (!background) workspace.OpenOrFocus(existing, CreateRestoreEntry(key));
            return;
        }

        var restoreEntry = CreateRestoreEntry(key);
        var restored = restoreEntry is null ? null
            : workspace.TakeTabHistory(restoreEntry.TabType, restoreEntry.Key)?.Restore();
        if ((restored ?? editorFactory.CreateEditor(key, this)) is { } editor)
            OpenTab(editor, restoreEntry, background);
    }

    private async Task CloseDetachedTabAsync(LiveDaqDetailViewModel tab)
    {
        try { await tab.PrepareCloseAsync(); }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to clean up replaced DAQ tab {IdentityKey}", tab.IdentityKey);
            await dispatcher.InvokeAsync(feedback.ShowUnexpectedFailure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (cleanupGate)
        {
            disposed = true;
            pending = [.. detachedTabCleanup];
        }
        await Task.WhenAll(pending).ConfigureAwait(false);
        lock (cleanupGate) detachedTabCleanup.Clear();
    }

    public async Task CloseEditorAsync(EditorKey key)
    {
        if (CreateRestoreEntry(key) is { } restoreEntry)
            workspace.ForgetTabHistory(restoreEntry.TabType, restoreEntry.Key);
        if (workspace.Tabs.FirstOrDefault(tab => Matches(key, tab)) is { } editor)
        {
            await editor.PrepareCloseAsync();
            workspace.CloseTab(editor, rememberForRestore: false);
        }
    }

    private ClosedTabRestoreEntry? CreateRestoreEntry(EditorKey key) => key switch
    {
        EditorKey.Bike bike => new(typeof(BikeEditorViewModel), bike.Id, () => editorFactory.CreateEditor(key, this)),
        EditorKey.Setup setup => new(typeof(SetupEditorViewModel), setup.Id, () => editorFactory.CreateEditor(key, this)),
        EditorKey.Session session => new(typeof(SessionDetailViewModel), session.Id, () => editorFactory.CreateEditor(key, this)),
        _ => null
    };

    private static bool Matches(EditorKey key, TabPageViewModelBase tab) => (key, tab) switch
    {
        (EditorKey.Bike keyBike, BikeEditorViewModel bike) => keyBike.Id == bike.Id,
        (EditorKey.Setup keySetup, SetupEditorViewModel setup) => keySetup.Id == setup.Id,
        (EditorKey.Session keySession, SessionDetailViewModel session) => keySession.Id == session.Id,
        (EditorKey.ImportSessions, ImportSessionsViewModel) => true,
        (EditorKey.LiveDaq keyDaq, LiveDaqDetailViewModel daq) => keyDaq.IdentityKey == daq.IdentityKey,
        (EditorKey.LiveSession keyLive, LiveSessionDetailViewModel live) => keyLive.IdentityKey == live.IdentityKey,
        _ => false
    };

    public void Open(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tab)
        {
            OpenTab(tab, restoreEntry: null, background: false);
        }
    }

    public void Close(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tab)
        {
            workspace.CloseTab(tab, ShouldRememberClosedTabs);
        }
    }

    public bool GoBack() => workspace.GoBack();

    public void CompleteEdit(EditorKey savedEditor)
    {
        if (appEnvironment.LayoutProfile == UiLayoutProfile.Compact &&
            workspace.CurrentTab is { } current && Matches(savedEditor, current))
        {
            workspace.GoBack();
        }
    }

    private void OpenTab(
        ViewModelBase view,
        ClosedTabRestoreEntry? restoreEntry,
        bool background)
    {
        if (view is not TabPageViewModelBase tab)
        {
            return;
        }

        if (background)
        {
            workspace.OpenInBackground(tab, restoreEntry);
            return;
        }

        workspace.OpenOrFocus(tab, restoreEntry);
    }

}
