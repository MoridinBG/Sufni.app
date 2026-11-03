using Sufni.App.Shell.ViewModels.Base;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shared.Base;
namespace Sufni.App.Tests.TestSupport.Doubles;

public sealed class TestViewModel : ViewModelBase
{
    public TestViewModel(TimeProvider? timeProvider = null)
        : base(new InlineUiThreadDispatcher(), timeProvider ?? new FakeTimeProvider())
    {
    }
}

public sealed class TestTabPageViewModel : TabPageViewModelBase
{
    public bool WasClosed { get; private set; }
    protected override bool CanSave() => IsDirty;
    protected override Task CloseImplementation()
    {
        WasClosed = true;
        return Task.CompletedTask;
    }

    public TestTabPageViewModel(IDialogService? dialogService = null)
        : base(Substitute.For<IShellCoordinator>(), dialogService ?? Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
    {
    }
}
