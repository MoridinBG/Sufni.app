using Sufni.App.Bikes.Coordinators;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class TestBikeSaveValidator : IBikeSaveValidator
{
    public Func<BikeDraft, Task<BikeSaveValidation>> Evaluate { get; set; } =
        _ => Task.FromResult<BikeSaveValidation>(new BikeSaveValidation.Valid());
    public Task<BikeSaveValidation> ValidateAsync(BikeDraft draft, CancellationToken cancellationToken = default) => Evaluate(draft);
}
