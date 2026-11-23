using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using NSubstitute;
using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class BikePhotoLifetimeTests : BikeEditorFixture
{
    [AvaloniaFact]
    public async Task UnloadingDuringDecodeReleasesTheDecodedPhoto()
    {
        var completion = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        var background = Substitute.For<IBackgroundTaskRunner>();
        background.RunAsync(Arg.Any<Func<Bitmap>>(), Arg.Any<CancellationToken>()).Returns(completion.Task);
        var editor = CreateEditor(TestSnapshots.Bike(), background: background);
        files.LoadImageAsync(Arg.Any<CancellationToken>()).Returns(new BikeImageLoadResult.Loaded(TestImages.SmallPngBytes(), "photo"));
        var open = editor.Linkage.OpenImageCommand.ExecuteAsync(null);
        editor.UnloadedCommand.Execute(null);
        var decoded = new ObservableBitmap();
        completion.SetResult(decoded);
        await open.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.True(decoded.Released);
        Assert.Null(editor.Linkage.Canvas.Image);
        await editor.PrepareCloseAsync();
    }

    [AvaloniaFact]
    public async Task ReplacingAPhotoReleasesTheOldBitmapBeforeExposingTheNewOne()
    {
        var first = new ObservableBitmap();
        var replacement = new ObservableBitmap();
        var background = Substitute.For<IBackgroundTaskRunner>();
        background.RunAsync(Arg.Any<Func<Bitmap>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Bitmap>(first), Task.FromResult<Bitmap>(replacement));
        var editor = CreateEditor(FullSuspensionSnapshot(), background: background);
        var canvas = editor.Linkage.Canvas;
        Assert.Same(first, canvas.Image);
        var observations = new List<Bitmap?>();
        canvas.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(canvas.Image)) observations.Add(canvas.Image);
        };
        files.LoadImageAsync(Arg.Any<CancellationToken>()).Returns(new BikeImageLoadResult.Loaded(TestImages.SmallPngBytes(), "replacement"));
        await editor.Linkage.OpenImageCommand.ExecuteAsync(null);

        Assert.True(first.Released);
        Assert.Contains(null, observations);
        Assert.Same(replacement, canvas.Image);
        Assert.False(replacement.Released);
        await editor.PrepareCloseAsync();
        Assert.True(replacement.Released);
    }

    private sealed class ObservableBitmap() : Bitmap(new MemoryStream(TestImages.SmallPngBytes()))
    {
        public bool Released { get; private set; }
        public override void Dispose()
        {
            Released = true;
            base.Dispose();
        }
    }
}
