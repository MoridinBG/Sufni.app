namespace Sufni.App.Shared.Models;

public enum StartupFailureReason { Database, Startup, Server, Reset }

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

public enum BikeSaveFailureReason { Save, Analysis }
public enum BikeDampingCutoffFailureReason { Save }
public enum BikeDeleteFailureReason { Delete }

public enum SetupSaveFailureReason { Save }
public enum SetupImportFailureReason { Import }
public enum SetupExportFailureReason { Export }
public enum SetupDeleteFailureReason { Delete }

public enum SessionSaveFailureReason { Save }
public enum SessionEditFailureReason { Edit, Rejected }
public enum GpsOffsetFailureReason { Save }

public enum SyncFailureReason { Synchronize, ProtocolMismatch, Interrupted }

public enum BikeAnalysisFailureReason { Analyze }

public enum BikeImportFailureReason { Import }
public enum BikeExportFailureReason { Export }
public enum BikeImageFailureReason { Read, Decode }
public enum LeverageRatioImportFailureReason { Import }

public enum PairingFailureReason { Request, Confirm, Unpair }

public enum DataStoreDiscoveryFailureReason { Scan, Connect }

public enum GpxImportFailureReason { InvalidFile, Import }

public enum MapLayerFailureReason { Add, Select }

public enum RearCalibrationFailureReason { Configuration, Calculation }

public enum SessionProcessingFailureReason { InvalidCalibration, Calibration, NoTravelData, InvalidRecording }
public enum SessionRecomputeFailureReason { Recompute, ProcessingOption }
public enum LiveSessionSaveFailureReason { Save, Publish, Cleanup, Preferences }
public enum SessionImportFailureReason { Import, InvalidFile, Publish, Trash, Acknowledge, Batch }

public enum SessionDeleteFailureReason { Delete }

public enum ServerStartFailureReason { Start }

public enum StravaConnectFailureReason { Connect }
public enum StravaSegmentFetchFailureReason { Load }

public enum SegmentWriteFailureReason { Create, Rename, Delete }

public enum DaqManagementFailureReason { SetTime, ReadConfig, WriteConfig, SelectConfigFile, InvalidConfigName }
