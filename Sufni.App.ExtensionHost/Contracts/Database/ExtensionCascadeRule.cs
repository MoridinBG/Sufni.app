namespace Sufni.App.ExtensionHost.Contracts.Database;

public enum ExtensionCoreEntityKind
{
    Board,
    Bike,
    Setup,
    Session,
    Track
}

public enum ExtensionCascadeAction
{
    SoftDelete,
    HardDelete
}

public sealed record ExtensionCascadeRule(
    string ExtensionId,
    ExtensionCoreEntityKind CoreEntityKind,
    string TableName,
    string ReferenceColumnName,
    ExtensionCascadeAction Action,
    string DeletedColumnName = "deleted",
    string UpdatedColumnName = "updated");

