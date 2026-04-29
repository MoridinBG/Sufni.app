using System.Text;
using Sufni.App.Services.Management;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.ViewModels.Editors;

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
                return Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok());
            });
        Row(editor, "TRAVEL_SAMPLE_RATE").Value = "0";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.False(uploadCalled);
        Assert.False(editor.IsCompleted);
        Assert.True(editor.HasValidationErrors);
        Assert.NotNull(Row(editor, "TRAVEL_SAMPLE_RATE").ValidationMessage);
    }

    [Fact]
    public async Task SaveCommand_UploadsCanonicalConfigBytes_AndCompletesOnSuccess()
    {
        byte[]? uploadedBytes = null;
        var editor = CreateEditor(
            DaqConfigDocument.Parse("""
# before
SSID=old-sta
PSK=old-psk
UNKNOWN=value
"""),
            (bytes, _) =>
            {
                uploadedBytes = bytes;
                return Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok());
            });
        var completions = 0;
        editor.Completed += (_, _) => completions++;
        Row(editor, "STA_SSID").Value = "edited-sta";
        Row(editor, "STA_PSK").Value = "edited-psk";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(uploadedBytes);
        var uploadedText = Encoding.UTF8.GetString(uploadedBytes);
        Assert.Contains("# before", uploadedText);
        Assert.Contains("UNKNOWN=value", uploadedText);
        Assert.Contains("STA_SSID=edited-sta", uploadedText);
        Assert.Contains("STA_PSK=edited-psk", uploadedText);
        var uploadedLines = uploadedText.Split('\n');
        Assert.DoesNotContain(uploadedLines, line => line.StartsWith("SSID=", StringComparison.Ordinal));
        Assert.DoesNotContain(uploadedLines, line => line.StartsWith("PSK=", StringComparison.Ordinal));
        Assert.True(editor.IsCompleted);
        Assert.Equal(1, completions);
    }

    [Fact]
    public async Task SaveCommand_KeepsEditorOpenAndShowsError_OnUploadFailure()
    {
        var editor = CreateEditor(
            DaqConfigDocument.Parse(string.Empty),
            (_, _) => Task.FromResult<DaqManagementResult>(
                new DaqManagementResult.Error(DaqManagementErrorCode.ValidationError, "invalid config")));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.False(editor.IsCompleted);
        Assert.True(editor.HasSaveError);
        Assert.Equal("invalid config", editor.SaveErrorMessage);
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
                return Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok());
            });
        var completions = 0;
        editor.Completed += (_, _) => completions++;

        editor.CancelCommand.Execute(null);

        Assert.False(uploadCalled);
        Assert.True(editor.IsCompleted);
        Assert.Equal(1, completions);
    }

    private static LiveDaqConfigEditorViewModel CreateEditor(
        DaqConfigDocument document,
        Func<byte[], CancellationToken, Task<DaqManagementResult>>? uploadAsync = null) =>
        new(document, uploadAsync ?? ((_, _) => Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok())));

    private static LiveDaqConfigFieldRowViewModel Row(LiveDaqConfigEditorViewModel editor, string key) =>
        editor.Fields.Single(row => row.Key == key);
}