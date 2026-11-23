using Sufni.App.Shared.Models;
using Microsoft.Extensions.Time.Testing;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.Kinematics;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class BikeDraftLoadControllerTests : BikeEditorFixture
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
        await editor.LoadedCommand.ExecuteAsync(null).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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
        await editor.LoadedCommand.ExecuteAsync(null).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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
