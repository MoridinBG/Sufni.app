using System.Reflection;
using Xunit.v3;

namespace Sufni.App.Tests.TestSupport.Harness;

/// <summary>
/// Holds back the next test until a disposed Mapsui map stops posting. Its render loop runs on the thread pool and
/// may post one more invalidate to the UI dispatcher a few milliseconds after disposal; a post that lands while the
/// next headless session resets the dispatcher binds it to the pool thread and breaks that session.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class MapsuiSettleAttribute : BeforeAfterTestAttribute
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);

    public override void After(MethodInfo methodUnderTest, IXunitTest test) => Thread.Sleep(Settle);
}
