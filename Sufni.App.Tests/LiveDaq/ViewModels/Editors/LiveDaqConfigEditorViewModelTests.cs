using System.Collections.Immutable;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.Shared.Models;
using System.Text;

using Sufni.App.Acquisition.Services.Management;
using Sufni.App.LiveDaq.ViewModels.Editors;
namespace Sufni.App.Tests.LiveDaq.ViewModels.Editors;

public class LiveDaqConfigEditorViewModelTests
{
    [Fact]
    public void Constructor_CreatesRowsInFirmwareOrder_WithDefaultsAndSecretFlags()
    {
        var editor = CreateEditor(DaqConfigDocument.Parse("STA_SSID=trail\n"));

        Assert.Equal(DaqConfigFields.All.Select(field => field.Key), editor.Fields.Select(row => row.Key));
        Assert.Equal("trail", Row(editor, "STA_SSID").Value);
        Assert.Equal("changemeplease", Row(editor, "STA_PSK").Value);
        Assert.Equal("pool.ntp.org", Row(editor, "NTP_SERVER").Value);
        Assert.True(Row(editor, "STA_PSK").IsSecret);
        Assert.True(Row(editor, "AP_PSK").IsSecret);
        Assert.False(Row(editor, "STA_PSK").IsSecretRevealed);
    }

    [Fact]
    public async Task SaveCommand_ValidatesBeforeUpload()
    {
        var uploadCalled = false;
        var editor = CreateEditor(
            DaqConfigDocument.Parse(string.Empty),
            (_, _) =>
            {
                uploadCalled = true;
                return Task.FromResult<LiveDaqManagementResult>(new LiveDaqManagementResult.ConfigWritten());
            });
        Row(editor, "TRAVEL_SAMPLE_RATE").Value = "0";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.False(uploadCalled);
        Assert.False(editor.IsCompleted);
        Assert.True(editor.HasValidationErrors);
        Assert.NotNull(Row(editor, "TRAVEL_SAMPLE_RATE").ValidationMessage);
    }

    [Fact]
    public async Task SaveCommand_SubmitsEditedValues_AndCompletesOnSuccess()
    {
        ImmutableDictionary<string, string>? submitted = null;
        var editor = CreateEditor(
            DaqConfigDocument.Parse("""
# before
SSID=old-sta
PSK=old-psk
UNKNOWN=value
"""),
            (values, _) =>
            {
                submitted = values;
                return Task.FromResult<LiveDaqManagementResult>(new LiveDaqManagementResult.ConfigWritten());
            });
        var completions = 0;
        editor.Completed += (_, _) => completions++;
        Row(editor, "STA_SSID").Value = "edited-sta";
        Row(editor, "STA_PSK").Value = "edited-psk";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(submitted);
        Assert.Equal("edited-sta", submitted["STA_SSID"]);
        Assert.Equal("edited-psk", submitted["STA_PSK"]);
        Assert.True(editor.IsCompleted);
        Assert.Equal(1, completions);
    }

    [Fact]
    public async Task SaveCommand_KeepsEditorOpenAndShowsError_OnUploadFailure()
    {
        const string uploadFailureMessage = "upload failure sentinel";
        var editor = CreateEditor(
            DaqConfigDocument.Parse(string.Empty),
            (_, _) => Task.FromResult<LiveDaqManagementResult>(
                new LiveDaqManagementResult.Rejected(DaqManagementErrorCode.ValidationError, uploadFailureMessage)));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.False(editor.IsCompleted);
        Assert.True(editor.HasSaveError);
        Assert.Equal(uploadFailureMessage, editor.SaveError!.Details);
        Assert.DoesNotContain(uploadFailureMessage, editor.SaveError.Summary);
    }

    [Fact]
    public void CancelCommand_CompletesEditorWithoutUploading()
    {
        var uploadCalled = false;
        var editor = CreateEditor(
            DaqConfigDocument.Parse(string.Empty),
            (_, _) =>
            {
                uploadCalled = true;
                return Task.FromResult<LiveDaqManagementResult>(new LiveDaqManagementResult.ConfigWritten());
            });
        var completions = 0;
        editor.Completed += (_, _) => completions++;

        editor.CancelCommand.Execute(null);

        Assert.False(uploadCalled);
        Assert.True(editor.IsCompleted);
        Assert.Equal(1, completions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Close_CancelsUploadAndRejectsLateFailure(bool cancelCommand)
    {
        var completion = new TaskCompletionSource<LiveDaqManagementResult>();
        CancellationToken token = default;
        using var editor = CreateEditor(DaqConfigDocument.Parse(string.Empty), (_, cancellation) =>
        {
            token = cancellation;
            return completion.Task;
        });
        var saving = editor.SaveCommand.ExecuteAsync(null);

        if (cancelCommand) editor.CancelCommand.Execute(null);
        else editor.Dispose();
        Assert.True(token.IsCancellationRequested);
        completion.SetResult(new LiveDaqManagementResult.Failed(DaqManagementFailureReason.WriteConfig, "late diagnostic"));
        await saving.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.False(editor.HasSaveError);
        Assert.False(editor.IsSaving);
    }

    private static LiveDaqConfigEditorViewModel CreateEditor(
        DaqConfigDocument document,
        Func<ImmutableDictionary<string, string>, CancellationToken, Task<LiveDaqManagementResult>>? uploadAsync = null) =>
        new(document, uploadAsync ?? ((_, _) => Task.FromResult<LiveDaqManagementResult>(new LiveDaqManagementResult.ConfigWritten())));

    private static LiveDaqConfigFieldRowViewModel Row(LiveDaqConfigEditorViewModel editor, string key) =>
        editor.Fields.Single(row => row.Key == key);
}
