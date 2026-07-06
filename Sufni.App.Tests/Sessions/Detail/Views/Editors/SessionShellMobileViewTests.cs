using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Detail.Views.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Sessions.Detail.Views.Editors;

[Collection("Ui")]
public class SessionShellMobileViewTests
{
    [AvaloniaFact]
    public async Task SessionShellMobileView_SelectionControlsUpdateWorkspaceIndex()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var carousel = mounted.Shell.FindControl<CarouselPage>("SessionCarouselPage")
            ?? throw new InvalidOperationException("Session carousel was not found.");
        var pager = mounted.Shell.FindControl<PipsPager>("SessionPipsPager")
            ?? throw new InvalidOperationException("Session pips pager was not found.");
        var header = mounted.Shell.FindControl<TextBlock>("SelectedPageHeader")
            ?? throw new InvalidOperationException("Selected page header was not found.");

        carousel.SelectedIndex = 1;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, host.SelectedPageIndex);
        Assert.Equal("Damping", header.Text);

        pager.SelectedPageIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, host.SelectedPageIndex);
        Assert.Equal("Notes", header.Text);

        host.SelectedPageIndex = 0;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(0, carousel.SelectedIndex);
        Assert.Equal(0, pager.SelectedPageIndex);
        Assert.Equal("Spring", header.Text);
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_OperationOverlay_TracksSessionOperationState()
    {
        var host = CreateHost();
        host.SessionOperationState = SessionOperationPresentationState.Progress("matching session 8/10", 80);
        await using var mounted = await MountAsync(host);

        var busyOverlay = mounted.Shell.FindControl<BusyOverlay>("SessionOperationBusyOverlay")
            ?? throw new InvalidOperationException("Session operation busy overlay was not found.");
        Assert.True(busyOverlay.IsActive);
        Assert.True(busyOverlay.IsVisible);
        Assert.Equal("matching session 8/10", busyOverlay.Message);
        Assert.Equal(0.8, busyOverlay.ProgressValue);

        host.SessionOperationState = SessionOperationPresentationState.Hidden;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(busyOverlay.IsActive);
        Assert.False(busyOverlay.IsVisible);
    }

    private static FakeShellHostViewModel CreateHost()
    {
        var host = new FakeShellHostViewModel
        {
            Name = "Test session",
            ScreenState = SessionScreenPresentationState.Ready,
        };
        host.Pages.Add(new SpringPageViewModel());
        host.Pages.Add(new DampingPageViewModel());
        host.Pages.Add(new NotesPageViewModel());
        return host;
    }

    private static async Task<MountedShell> MountAsync(FakeShellHostViewModel host)
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

        var window = await ViewTestHelpers.ShowViewAsync(shell);
        return new MountedShell(window, shell);
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

internal sealed partial class FakeShellHostViewModel : ViewModelBase, ISessionShellMobileWorkspace
{
    public FakeShellHostViewModel()
        : base(new InlineUiThreadDispatcher())
    {
        Pages.CollectionChanged += OnPagesChanged;
    }

    public TabPageViewModelBase Editor { get; } = new FakeTabPageViewModel(new InlineUiThreadDispatcher());

    public ObservableCollection<PageViewModelBase> Pages { get; } = [];

    private int selectedPageIndex;

    public int SelectedPageIndex
    {
        get => selectedPageIndex;
        set => SetSelectedPageIndex(value);
    }

    public PageViewModelBase? SelectedPage => Pages.Count == 0 ? null : Pages[SelectedPageIndex];

    public int PageCount => Pages.Count;

    public string SelectedPageDisplayName => SelectedPage?.DisplayName ?? string.Empty;

    private sealed class FakeTabPageViewModel(IUiThreadDispatcher uiThreadDispatcher)
        : TabPageViewModelBase(uiThreadDispatcher);

    [ObservableProperty]
    public partial SessionScreenPresentationState ScreenState { get; set; } = SessionScreenPresentationState.Ready;

    [ObservableProperty]
    public partial SessionOperationPresentationState SessionOperationState { get; set; } = SessionOperationPresentationState.Hidden;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial DateTime? Timestamp { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [RelayCommand]
    private void Loaded(Rect? bounds) { }

    [RelayCommand]
    private void Unloaded() { }

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

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var clampedIndex = ClampSelectedPageIndex(selectedPageIndex);
        SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex));
        NotifySelectedPagePropertiesChanged();
    }

    private void SetSelectedPageIndex(int value)
    {
        var clampedIndex = ClampSelectedPageIndex(value);
        if (SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex)))
        {
            NotifySelectedPagePropertiesChanged();
        }
    }

    private int ClampSelectedPageIndex(int value)
    {
        if (Pages.Count == 0)
        {
            return 0;
        }

        if (value < 0)
        {
            return 0;
        }

        if (value >= Pages.Count)
        {
            return Pages.Count - 1;
        }

        return value;
    }

    private void NotifySelectedPagePropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(SelectedPageDisplayName));
    }
}
