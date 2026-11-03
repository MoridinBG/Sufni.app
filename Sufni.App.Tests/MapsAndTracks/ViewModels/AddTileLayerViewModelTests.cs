using Sufni.App.MapsAndTracks.ViewModels;

namespace Sufni.App.Tests.MapsAndTracks.ViewModels;

public class AddTileLayerViewModelTests
{
    [Theory]
    [InlineData(null, "https://example/{z}/{x}/{y}")]
    [InlineData("Layer", " ")]
    public void Confirm_IncompleteDraftKeepsDialogOpen(string? name, string? url)
    {
        var draft = new AddTileLayerViewModel { Name = name, UrlTemplate = url };
        var completed = false;
        draft.Completed += (_, _) => completed = true;

        draft.ConfirmCommand.Execute(null);

        Assert.False(completed);
        Assert.Null(draft.ConfirmedLayer);
    }

    [Fact]
    public void Cancel_ClosesDialogWithoutAcceptingDraft()
    {
        var draft = new AddTileLayerViewModel { Name = "Layer", UrlTemplate = "https://example/{z}/{x}/{y}" };
        var completed = false;
        draft.Completed += (_, _) => completed = true;

        draft.CancelCommand.Execute(null);

        Assert.True(completed);
        Assert.Null(draft.ConfirmedLayer);
    }
}
