using System.Reactive;
using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Terrain.Queries;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal static class TestTerrainLabelQueries
{
    /// <summary>A label query whose setups have no session, no label and no change.</summary>
    public static ITerrainLabelQuery Empty()
    {
        var query = Substitute.For<ITerrainLabelQuery>();
        query.ForSetup(Arg.Any<Guid>()).Returns(new SetupTerrainLabels(null, []));
        query.Changes.Returns(Observable.Never<Unit>());
        return query;
    }
}
