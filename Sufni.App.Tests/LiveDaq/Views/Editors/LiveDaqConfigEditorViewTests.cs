using System.Collections.Immutable;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.Shared.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;

using Sufni.App.Acquisition.Services.Management;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.LiveDaq.Views.Editors;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.LiveDaq.Views.Editors;

[Collection("Ui")]
public class LiveDaqConfigEditorViewTests
{
    private static LiveDaqConfigEditorViewModel CreateEditor(
        Func<ImmutableDictionary<string, string>, CancellationToken, Task<LiveDaqManagementResult>>? uploadAsync = null) =>
        new(
            DaqConfigDocument.Parse("""
STA_SSID=trail
STA_PSK=changemeplease
AP_PSK=changemeplease
"""),
            uploadAsync ?? ((_, _) => Task.FromResult<LiveDaqManagementResult>(new LiveDaqManagementResult.ConfigWritten())));

    private static async Task<MountedLiveDaqConfigEditorView> MountAsync(
        LiveDaqConfigEditorViewModel editor,
        double width = 640,
        double height = 720)
    {
        ViewTestHelpers.EnsureViewTestResources();
        EnsureFluentTheme();
        var view = new LiveDaqConfigEditorView
        {
            DataContext = editor
        };
        var host = new Window
        {
            Width = width,
            Height = height,
            Content = view
        };

        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedLiveDaqConfigEditorView(host, view);
    }

    private static void EnsureFluentTheme()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Add(new FluentTheme());
        }
    }
}

internal sealed class MountedLiveDaqConfigEditorView(Window host, LiveDaqConfigEditorView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqConfigEditorView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}