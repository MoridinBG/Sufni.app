namespace Sufni.App.Acquisition.Models;

/// <summary>A captured import decision; subsequent row edits cannot change a running batch.</summary>
public sealed record SessionImportItem(ITelemetryFile File, string Name, string Description, ImportFileAction Action);
