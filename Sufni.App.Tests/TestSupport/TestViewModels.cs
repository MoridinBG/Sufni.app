
using Sufni.App.Shared.Base;
namespace Sufni.App.Tests.TestSupport;

public sealed class TestViewModel : ViewModelBase
{
    public TestViewModel()
        : base(new InlineUiThreadDispatcher())
    {
    }
}

public sealed class TestTabPageViewModel : TabPageViewModelBase
{
    public TestTabPageViewModel()
        : base(new InlineUiThreadDispatcher())
    {
    }
}
