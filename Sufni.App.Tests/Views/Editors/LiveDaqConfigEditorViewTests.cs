using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Sufni.App.Services.Management;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class LiveDaqConfigEditorViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqConfigEditorView_RendersAllKnownRows()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var visibleText = mounted.View.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        foreach (var field in DaqConfigFields.All)
        {
            Assert.Contains(field.Key, visibleText);
        }

        Assert.Equal(
            DaqConfigFields.All.Count - 1,
            mounted.View.GetVisualDescendants().OfType<TextBox>().Count(textBox => textBox.Name == "FieldValueTextBox" && textBox.IsVisible));
        Assert.Single(
            mounted.View.GetVisualDescendants().OfType<ComboBox>(),
            comboBox => comboBox.Name == "WifiModeComboBox" && comboBox.IsVisible);
    }

    [AvaloniaFact]
    public async Task LiveDaqConfigEditorView_PskRows_AreMaskedAndHaveRevealControls()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var pskTextBoxes = mounted.View.GetVisualDescendants()
            .OfType<TextBox>()
            .Where(textBox => textBox.Text == "changemeplease")
            .ToArray();
        var revealButtons = mounted.View.GetVisualDescendants().OfType<ToggleButton>().Where(button => button.IsVisible).ToArray();

        Assert.Equal(2, pskTextBoxes.Length);
        Assert.All(pskTextBoxes, textBox => Assert.Equal('*', textBox.PasswordChar));
        Assert.Equal(2, revealButtons.Length);
        Assert.All(revealButtons, button => Assert.Equal("Show", button.Content));
    }

    [AvaloniaFact]
    public async Task LiveDaqConfigEditorView_ShowsValidationAndUploadErrors()
    {
        var validationEditor = CreateEditor();
        await using var validationMount = await MountAsync(validationEditor);
        validationEditor.Fields.Single(row => row.Key == "STA_SSID").Value = string.Empty;
        await ViewTestHelpers.FlushDispatcherAsync();

        var validationMessage = validationEditor.Fields.Single(row => row.Key == "STA_SSID").ValidationMessage;
        Assert.NotNull(validationMessage);
        Assert.Contains(
            validationMount.View.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == validationMessage && textBlock.IsVisible);

        var uploadEditor = CreateEditor((_, _) => Task.FromResult<DaqManagementResult>(
            new DaqManagementResult.Error(DaqManagementErrorCode.ValidationError, "invalid config")));
        await using var uploadMount = await MountAsync(uploadEditor);

        await uploadEditor.SaveCommand.ExecuteAsync(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        var saveError = uploadMount.View.FindControl<TextBlock>("SaveErrorTextBlock")!;
        Assert.True(saveError.IsVisible);
        Assert.Equal("invalid config", saveError.Text);
    }

    [AvaloniaFact]
    public async Task LiveDaqConfigEditorView_ActionButtons_AreReachableOnNarrowLayout()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor, width: 360, height: 640);

        var cancelButton = mounted.View.FindControl<Button>("CancelButton")!;
        var saveButton = mounted.View.FindControl<Button>("SaveButton")!;
        Assert.True(cancelButton.IsVisible);
        Assert.True(saveButton.IsVisible);
        Assert.Same(editor.CancelCommand, cancelButton.Command);
        Assert.Same(editor.SaveCommand, saveButton.Command);
    }

    private static LiveDaqConfigEditorViewModel CreateEditor(
        Func<byte[], CancellationToken, Task<DaqManagementResult>>? uploadAsync = null) =>
        new(
            DaqConfigDocument.Parse("""
STA_SSID=trail
STA_PSK=changemeplease
AP_PSK=changemeplease
"""),
            uploadAsync ?? ((_, _) => Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok())));

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

        host.Show();
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