using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.Presentation;

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
    public async Task SessionShellMobileView_RendersChromeFromPagesBinding()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var carousel = mounted.Shell.FindControl<CarouselPage>("SessionCarouselPage");
        var pager = mounted.Shell.FindControl<PipsPager>("SessionPipsPager");
        var header = mounted.Shell.FindControl<TextBlock>("SelectedPageHeader");

        Assert.NotNull(carousel);
        Assert.Same(host.Pages, carousel!.ItemsSource);
        Assert.NotNull(carousel.PageTemplate);
        Assert.Equal(host.SelectedPageIndex, carousel.SelectedIndex);
        Assert.NotNull(pager);
        Assert.Equal(host.PageCount, pager!.NumberOfPages);
        Assert.NotNull(header);
        Assert.Equal(host.SelectedPageDisplayName, header!.Text);
        Assert.NotNull(mounted.Shell.GetVisualDescendants().OfType<EditableTitle>().FirstOrDefault());
        Assert.NotNull(mounted.Shell.GetVisualDescendants().OfType<ErrorMessagesBar>().FirstOrDefault());
        var buttonLine = mounted.Shell.GetVisualDescendants().OfType<CommonButtonLine>().FirstOrDefault();
        Assert.NotNull(buttonLine);
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_PageTemplate_CreatesSafeAreaFreeSessionContentPage()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var carousel = mounted.Shell.FindControl<CarouselPage>("SessionCarouselPage")
            ?? throw new InvalidOperationException("Session carousel was not found.");
        var pageTemplate = carousel.PageTemplate
            ?? throw new InvalidOperationException("Session page template was not found.");
        var pageViewModel = host.Pages[0];

        var contentPage = Assert.IsType<ContentPage>(pageTemplate.Build(pageViewModel));
        contentPage.DataContext = pageViewModel;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(contentPage.AutomaticallyApplySafeAreaPadding);
        Assert.Equal(pageViewModel.DisplayName, contentPage.Header);
        Assert.Same(pageViewModel, contentPage.Content);
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
    public async Task SessionShellMobileView_UsesCarouselAndPagerInsteadOfLegacyTabControls()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        Assert.NotNull(mounted.Shell.FindControl<CarouselPage>("SessionCarouselPage"));
        Assert.NotNull(mounted.Shell.FindControl<PipsPager>("SessionPipsPager"));
        Assert.Null(mounted.Shell.FindControl<ItemsControl>("TabHeaders"));
        Assert.Null(mounted.Shell.FindControl<ScrollViewer>("TabScrollViewer"));
        Assert.Null(mounted.Shell.FindControl<ItemsControl>("TabContainer"));
    }

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
        Assert.Equal("Damper", header.Text);

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
    public async Task SessionShellMobileView_PagesMutationUpdatesPagerCount()
    {
        var host = CreateHost();
        await using var mounted = await MountAsync(host);

        var pager = mounted.Shell.FindControl<PipsPager>("SessionPipsPager")
            ?? throw new InvalidOperationException("Session pips pager was not found.");

        var extraPage = new BalancePageViewModel();
        host.Pages.Insert(0, extraPage);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(host.PageCount, pager.NumberOfPages);
        Assert.Same(extraPage, host.SelectedPage);
    }

    [AvaloniaFact]
    public async Task SessionShellMobileView_ChromeOverlay_TracksScreenState()
    {
        var host = CreateHost();
        host.ScreenState = SessionScreenPresentationState.Loading("loading test");
        await using var mounted = await MountAsync(host);

        var busyOverlay = mounted.Shell.FindControl<BusyOverlay>("ScreenBusyOverlay")
            ?? throw new InvalidOperationException("Screen busy overlay was not found.");
        Assert.True(busyOverlay.IsActive);
        Assert.True(busyOverlay.IsVisible);
        Assert.True(busyOverlay.UseStackLayout);
        Assert.Equal("loading test", busyOverlay.Message);

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
        Assert.False(busyOverlay.IsActive);
        Assert.False(busyOverlay.IsVisible);
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
        Assert.True(busyOverlay.ShowProgress);
        Assert.True(busyOverlay.ShowTint);
        Assert.Equal("matching session 8/10", busyOverlay.Message);
        Assert.NotNull(busyOverlay.MessageForeground);
        Assert.Equal(0.8, busyOverlay.ProgressValue);

        var operationMessage = mounted.Shell.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Name == "StackBusyMessageText");
        Assert.NotNull(operationMessage);
        Assert.Equal("matching session 8/10", operationMessage.Text);
        Assert.NotNull(operationMessage.Foreground);

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
        host.Pages.Add(new DamperPageViewModel());
        host.Pages.Add(new NotesPageViewModel());
        return host;
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
