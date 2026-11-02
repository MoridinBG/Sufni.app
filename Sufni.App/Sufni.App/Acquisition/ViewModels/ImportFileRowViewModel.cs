using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Acquisition.Models;

namespace Sufni.App.Acquisition.ViewModels;

/// <summary>Editable import choices over a device file's immutable listing metadata.</summary>
public sealed partial class ImportFileRowViewModel : ObservableObject
{
    public ImportFileRowViewModel(ITelemetryFile file)
    {
        File = file;
        Name = file.FileName;
        Description = $"Imported from {file.FileName}";
    }

    public ITelemetryFile File { get; }
    public string FileName => File.FileName;
    public DateTime StartTime => File.StartTime;
    public string Duration => File.Duration;
    public string? MalformedMessage => File.MalformedMessage;
    public bool CanImport => File.CanImport;

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Description { get; set; }
    [ObservableProperty] public partial ImportFileAction Action { get; set; }

    public SessionImportItem ToImportItem() => new(File, Name, Description, Action);
}
