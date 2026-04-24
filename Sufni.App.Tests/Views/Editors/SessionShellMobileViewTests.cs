using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.App.Views.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class SessionShellMobileViewTests
{
    [AvaloniaFact]
    public async Task SessionShellMobileView_RendersChromeFromPagesBinding()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var tabHeaders = mounted.Shell.GetVisualDescendants()
            .OfType<ItemsControl>()
            .First(c => c.Name == "TabHeaders");

        Assert.Equal(host.Pages.Count, tabHeaders.ItemCount);
        Assert.NotNull(mounted.Shell.GetVisualDescendants().OfType<EditableTitle>().FirstOrDefault());
        Assert.NotNull(mounted.Shell.GetVisualDescendants().OfType<ErrorMessagesBar>().FirstOrDefault());
        Assert.NotNull(mounted.Shell.GetVisualDescendants().OfType<CommonButtonLine>().FirstOrDefault());
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_LoadedCommand_FiresWithNonNullRectParameter()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        Assert.NotNull(host.LoadedRect);
        Assert.True(host.LoadedRect!.Value.Width > 0);
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_UnloadedCommandProperty_BindsToHostCommand()
    {
        // Avalonia's headless Unloaded event firing is unreliable in xunit;
        // verify the binding wiring instead (parallel to LoadedCommand,
        // which IS exercised end-to-end above).
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        Assert.Same(host.UnloadedCommand, mounted.Shell.UnloadedCommand);
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_ControlContent_IsHidden_WhenUnset()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var slot = mounted.Shell.GetVisualDescendants()
            .OfType<ContentControl>()
            .First(c => c.Name == "ShellControlContent");

        Assert.Null(slot.Content);
        Assert.False(slot.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_ControlContent_RendersWhenSet()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var injected = new TextBlock { Name = "InjectedControl", Text = "live-controls" };
        mounted.Shell.ControlContent = injected;
        await ViewTestHelpers.FlushDispatcherAsync();

        var slot = mounted.Shell.GetVisualDescendants()
            .OfType<ContentControl>()
            .First(c => c.Name == "ShellControlContent");

        Assert.True(slot.IsVisible);
        Assert.Same(injected, slot.Content);
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_PagesMutation_PreservesCurrentSelection()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var damperPage = host.Pages.OfType<DamperPageViewModel>().Single();
        foreach (var page in host.Pages)
        {
            page.Selected = page == damperPage;
        }
        await ViewTestHelpers.FlushDispatcherAsync();

        // Insert a new page — Damper should remain selected, and nothing else
        // should become selected by the CollectionChanged handler.
        var extraPage = new BalancePageViewModel();
        var notesPage = host.Pages.OfType<NotesPageViewModel>().Single();
        host.Pages.Insert(host.Pages.IndexOf(notesPage), extraPage);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(damperPage.Selected);
        Assert.All(
            host.Pages.Where(page => page != damperPage),
            page => Assert.False(page.Selected));
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_PagesMutation_WithNoSelection_SeedsFirstPageSelected()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        // Clear any initial selection, then insert a page.
        foreach (var page in host.Pages)
        {
            page.Selected = false;
        }

        var extraPage = new BalancePageViewModel();
        host.Pages.Insert(0, extraPage);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(host.Pages[0].Selected);
        Assert.All(host.Pages.Skip(1), page => Assert.False(page.Selected));
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_ChromeOverlay_TracksScreenState()
    {
        var host = CreateHost();
        host.ScreenState = SessionScreenPresentationState.Loading("loading test");
        await using var mounted = await MountAsync(host);

        var loadingMessage = mounted.Shell.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "loading test");
        Assert.NotNull(loadingMessage);

        host.ScreenState = SessionScreenPresentationState.Error("boom");
        await ViewTestHelpers.FlushDispatcherAsync();

        var errorHeading = mounted.Shell.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Could not load session");
        var errorMessage = mounted.Shell.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "boom");
        Assert.NotNull(errorHeading);
        Assert.NotNull(errorMessage);
    }

    private static FakeShellHostViewModel CreateHost()
    {
        return new FakeShellHostViewModel
        {
            Name = "Test session",
            Pages =
            [
                new SpringPageViewModel(),
                new DamperPageViewModel(),
                new NotesPageViewModel(),
            ],
            ScreenState = SessionScreenPresentationState.Ready,
        };
    }

    private static async Task<MountedShell> MountAsync(FakeShellHostViewModel host)
    {
        var window = await ShowAsync(host);
        var shell = window.GetVisualDescendants().OfType<SessionShellMobileView>().Single();
        return new MountedShell(window, shell);
    }

    private static async Task<Window> ShowAsync(FakeShellHostViewModel host)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var shell = new SessionShellMobileView
        {
            DataContext = host,
        };
        shell.Bind(SessionShellMobileView.LoadedCommandProperty,
            new Avalonia.Data.Binding(nameof(FakeShellHostViewModel.LoadedCommand)));
        shell.Bind(SessionShellMobileView.UnloadedCommandProperty,
            new Avalonia.Data.Binding(nameof(FakeShellHostViewModel.UnloadedCommand)));

        return await ViewTestHelpers.ShowViewAsync(shell);
    }

    private sealed record MountedShell(Window Host, SessionShellMobileView Shell) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}

internal sealed partial class FakeShellHostViewModel : ViewModelBase
{
    public ObservableCollection<PageViewModelBase> Pages { get; init; } = [];

    [ObservableProperty]
    private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;

    [ObservableProperty]
    private string? name;

    [ObservableProperty]
    private DateTime? timestamp;

    [ObservableProperty]
    private bool isDirty;

    public Rect? LoadedRect { get; private set; }
    public bool UnloadedFired { get; private set; }

    [RelayCommand]
    private void Loaded(Rect? bounds)
    {
        LoadedRect = bounds;
    }

    [RelayCommand]
    private void Unloaded()
    {
        UnloadedFired = true;
    }

    [RelayCommand]
    private void Save() { }

    [RelayCommand]
    private void Reset() { }

    [RelayCommand]
    private void Export() { }

    [RelayCommand]
    private void Delete(bool navigateBack) { }

    [RelayCommand]
    private void Close() { }
}
