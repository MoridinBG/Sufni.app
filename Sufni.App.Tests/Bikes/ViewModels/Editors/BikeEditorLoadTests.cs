using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class BikeEditorLoadTests : BikeEditorFixture
{
    [AvaloniaFact]
    public async Task ImageDraft_WaitsForBackgroundDecodeBeforeEnablingSave()
    {
        var background = new DeferredBackgroundTaskRunner();
        var snapshot = FullSuspensionSnapshot();
        var editor = CreateEditor(snapshot, background: background);
        Assert.Equal(BikeEditorPhase.Loading, editor.Phase);
        Assert.Null(editor.Linkage.Canvas.Image);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.ExportCommand.CanExecute(null));

        await background.RunNextAsync();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(editor.Linkage.Canvas.Image);
        Assert.Equal(BikeEditorPhase.Ready, editor.Phase);
        Assert.False(editor.IsDirty);
        editor.Name = "edited";
        Assert.True(editor.SaveCommand.CanExecute(null));
        await draftLoader.Received(1).LoadDraftAsync(snapshot.Id, Arg.Any<CancellationToken>());
        await editor.PrepareCloseAsync();
        Assert.Null(editor.Linkage.Canvas.Image);
    }

    [AvaloniaFact]
    public async Task ImageDraft_LoadBelongsToEditorAcrossViewDetach()
    {
        var background = new DeferredBackgroundTaskRunner();
        var editor = CreateEditor(FullSuspensionSnapshot(), background: background);
        editor.UnloadedCommand.Execute(null);

        await background.RunNextAsync();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(editor.Linkage.Canvas.Image);
        Assert.False(editor.IsDirty);
        await editor.PrepareCloseAsync();
    }

    [AvaloniaFact]
    public async Task Close_CancelsPendingImageDecodeAndDoesNotPublishIt()
    {
        var background = new DeferredBackgroundTaskRunner();
        var editor = CreateEditor(FullSuspensionSnapshot(), background: background);

        var close = editor.PrepareCloseAsync();
        await background.RunNextAsync();
        await close.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Null(editor.Linkage.Canvas.Image);
    }
}
