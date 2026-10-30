namespace Sufni.App.Shared.Models;

public enum StartupFailureReason { Database, Startup, Reset }

public enum VideoImportFailureReason
{
    LocalFileRequired,
    InvalidMetadata,
    Import
}

public enum VideoAlignmentFailureReason
{
    Save
}

public enum PairedDeviceUnpairFailureReason
{
    Unpair
}

public enum PendingDeleteFailureReason
{
    Delete
}
