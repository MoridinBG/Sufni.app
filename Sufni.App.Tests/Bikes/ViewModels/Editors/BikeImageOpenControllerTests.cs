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
public class BikeImageOpenControllerTests : BikeEditorFixture
{
    [AvaloniaFact]
    public async Task ReplacingAnUnreadableInitialPhotoRecoversAnEditableDraft()
    {
        var snapshot = FullSuspensionSnapshot(imageRotationDegrees: 12.5);
        var background = Substitute.For<IBackgroundTaskRunner>();
        background.RunAsync(Arg.Any<Func<Avalonia.Media.Imaging.Bitmap>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<Avalonia.Media.Imaging.Bitmap>(new IOException()),
                call => Task.FromResult(call.Arg<Func<Avalonia.Media.Imaging.Bitmap>>()()));
        var editor = CreateEditor(snapshot, background: background, storedImage: [0]);
        Assert.Equal(BikeEditorPhase.Unavailable, editor.Phase);
        files.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(new BikeImageLoadResult.Loaded(TestImages.SmallPngBytes(), "replacement.png"));

        await editor.Linkage.OpenImageCommand.ExecuteAsync(null);

        Assert.Equal(BikeEditorPhase.Ready, editor.Phase);
        Assert.NotNull(editor.Linkage.Canvas.Image);
        Assert.Equal(snapshot.ImageRotationDegrees, editor.Linkage.ImageRotationDegrees);
        Assert.True(editor.IsDirty);
        Assert.True(editor.Analysis.IsPlotBusy);
        await editor.PrepareCloseAsync();
    }

    [AvaloniaFact]
    public async Task OpenImage_DecodesSelectedBytes_AndPreservesRotation()
    {
        var snapshot = FullSuspensionSnapshot(imageRotationDegrees: 12.5);
        var editor = CreateEditor(snapshot);
        var imageBytes = TestImages.SmallPngBytes();

        files.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(
                new BikeImageLoadResult.Loaded(imageBytes, "bike.png")));

        await editor.Linkage.OpenImageCommand.ExecuteAsync(null);

        Assert.NotNull(editor.Linkage.Canvas.Image);
        Assert.Equal(1, editor.Linkage.Canvas.Image.Size.Width);
        Assert.Equal(1, editor.Linkage.Canvas.Image.Size.Height);
        Assert.Equal(12.5, editor.Linkage.ImageRotationDegrees);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task OpenImage_DropsPendingResult_WhenEditorIsUnloaded()
    {
        var editor = CreateEditor(TestSnapshots.Bike());
        var pendingImage = new TaskCompletionSource<BikeImageLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        files.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(pendingImage.Task);

        var openImageTask = editor.Linkage.OpenImageCommand.ExecuteAsync(null);
        editor.UnloadedCommand.Execute(null);
        pendingImage.SetResult(new BikeImageLoadResult.Loaded(TestImages.SmallPngBytes(), "bike.png"));
        await openImageTask;

        Assert.Null(editor.Linkage.Canvas.Image);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task OpenImage_PreservesCurrentImage_AndSurfacesFailure_WhenBytesAreInvalid()
    {
        var snapshot = FullSuspensionSnapshot(imageRotationDegrees: 12.5);
        var editor = CreateEditor(snapshot);
        var currentImage = editor.Linkage.Canvas.Image;
        files.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(
                new BikeImageLoadResult.Loaded([], "invalid.png")));

        await editor.Linkage.OpenImageCommand.ExecuteAsync(null);

        Assert.Same(currentImage, editor.Linkage.Canvas.Image);
        Assert.Equal(12.5, editor.Linkage.ImageRotationDegrees);
        Assert.NotEmpty(editor.ErrorMessages);
    }
}
