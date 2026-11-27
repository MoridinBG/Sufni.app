using Sufni.App.Terrain.ViewModels;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.ViewModels;

public class TerrainLabelClassPromptViewModelTests
{
    private static readonly TerrainLabelClass Rough = new TerrainLabelClass.Roughness(RoughnessClass.Rough);

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, "The range overlaps a roughness label.", false)]
    [InlineData(true, null, true)]
    public void ConfirmNeedsAClassAndNoValidationMessage(bool selected, string? message, bool canConfirm)
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label roughness", "Label", TerrainLabelKind.Roughness,
            selected ? Rough : null, message);

        Assert.Equal(canConfirm, prompt.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmCompletesWithTheSelectedClass()
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label roughness", "Label", TerrainLabelKind.Roughness, null, null);
        var completed = false;
        prompt.Completed += (_, _) => completed = true;

        prompt.SelectedClass = new TerrainLabelClass.Roughness(RoughnessClass.TechnicalClimb);
        prompt.ConfirmCommand.Execute(null);

        Assert.True(completed);
        Assert.Equal(new TerrainLabelClass.Roughness(RoughnessClass.TechnicalClimb), prompt.ConfirmedClass);
    }

    [Fact]
    public void CancelCompletesWithoutAClass()
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label roughness", "Label", TerrainLabelKind.Roughness, Rough, null);
        var completed = false;
        prompt.Completed += (_, _) => completed = true;

        prompt.CancelCommand.Execute(null);

        Assert.True(completed);
        Assert.Null(prompt.ConfirmedClass);
    }

    [Fact]
    public void RoughnessClassesListEveryRoughnessClass()
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label roughness", "Label", TerrainLabelKind.Roughness, null, null);

        Assert.Equal(Enum.GetValues<RoughnessClass>().Select(level => new TerrainLabelClass.Roughness(level)),
            prompt.Classes.Select(option => option.Class));
    }

    [Fact]
    public void SteepnessClassesListEveryGradeBand()
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label steepness", "Label", TerrainLabelKind.Steepness, null, null);

        Assert.Equal(Enum.GetValues<GradeBand>().Select(band => new TerrainLabelClass.Steepness(band)),
            prompt.Classes.Select(option => option.Class));
    }
}
