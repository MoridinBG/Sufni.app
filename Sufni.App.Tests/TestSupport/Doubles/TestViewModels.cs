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
    public TestTabPageViewModel()
        : base(Substitute.For<IShellCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
    {
    }
}
