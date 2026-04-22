using Sufni.App.ViewModels;

namespace Sufni.App.Tests.ViewModels;

public class TabPageViewModelBaseTests
{
    [Fact]
    public async Task SaveCommand_ReevaluatesDirtyState_WhenSaveDoesNotResolveChanges()
    {
        var viewModel = new DirtyTrackingTabPageViewModel();

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public async Task SaveCommand_LeavesPageClean_WhenSaveClearsUnderlyingDirtyState()
    {
        var viewModel = new DirtyTrackingTabPageViewModel();
        viewModel.OnSave = () =>
        {
            viewModel.UnderlyingDirty = false;
            return Task.CompletedTask;
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task ResetCommand_ReevaluatesDirtyState_WhenResetDoesNotResolveChanges()
    {
        var viewModel = new DirtyTrackingTabPageViewModel();

        await viewModel.ResetCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDirty);
    }

    private sealed class DirtyTrackingTabPageViewModel : TabPageViewModelBase
    {
        public bool UnderlyingDirty { get; set; } = true;

        public Func<Task>? OnSave { get; set; }

        protected override void EvaluateDirtiness()
        {
            IsDirty = UnderlyingDirty;
        }

        protected override Task SaveImplementation()
        {
            return OnSave?.Invoke() ?? Task.CompletedTask;
        }
    }
}