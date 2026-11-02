using Sufni.App.Bikes.Coordinators;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Bikes.Coordinators;

public class BikeDraftTests
{
    [Fact]
    public void ImageBytes_CannotBeMutatedThroughConstructorOrGetter()
    {
        byte[] bytes = [1, 2, 3];
        var draft = BikeDraft.From(TestSnapshots.Bike(), bytes);
        bytes[0] = 9;
        draft.ImageBytes[1] = 8;
        Assert.Equal([1, 2, 3], draft.ImageBytes);
    }

    [Fact]
    public void Equality_ComparesImageContentsForSaveConflicts()
    {
        var draft = BikeDraft.From(TestSnapshots.Bike(), [1, 2, 3]);
        Assert.Equal(draft, draft with { ImageBytes = [1, 2, 3] });
        Assert.NotEqual(draft, draft with { ImageBytes = [1, 4, 3] });
    }
}
