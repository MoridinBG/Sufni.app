DO NOT EDIT CODE UNLESS ASKED TO. NO SELF INITIATIVE!
GEMINI DO NOT EDIT CODE UNLESS EXPLICITLY TOLD TO

CRITICAL: These phrases are NOT permission to edit code:

- "I want to eventually..."
- "I'm thinking about..."
- "How would I..."
- "What if I..."
  And similar questions and inquiries

ONLY edit code when I explicitly say:

- "Please implement..."
- "Go ahead and..."
- "Make the changes"
- "I approve"
- "I want you to change..."

GEMINI THIS APPLIES TO YOU!!! DO NOT EDIT CODE UNLESS TOLD TO!!!

NEVER make assumptions about naming, paths, flows or code. Always read the code or configurations or ask the user.
If you repeatedly fail to achieve something, ask the user for direction. NEVER make more than two attempts.
NEVER queue several consecutive requests that retry user aproval. Always be ready to be denied.
NEVER add functionality on your initiative, unless the user asked - commenting, logging, failsafes. Ask the user if it might really be needed.
Don't change things not related to the current prompt.

File paths are relative to the repository root (Sufni.App/).

1. PROJECT STRUCTURE

The solution consists of multiple projects organized by responsibility:

CORE LIBRARIES
--------------

Sufni.Telemetry (Sufni.Telemetry/)
Pure C# library containing all telemetry data processing logic.
No platform dependencies. Key components:

    Sufni.Telemetry/TelemetryData.cs
        Main processing class - converts raw data to analyzed telemetry

    Sufni.Telemetry/RawTelemetryData.cs
        SST file parsing and spike elimination algorithms

    Sufni.Telemetry/Strokes.cs
        Stroke detection and categorization (compression/rebound/idling)

    Sufni.Telemetry/Filters.cs
        Signal processing filters (Savitzky-Golay)

    Sufni.Telemetry/Parameters.cs
        Processing constants and thresholds

Sufni.Kinematics (Sufni.Kinematics/)
Suspension linkage simulation library.
Models bike geometry and calculates leverage ratios.

    Sufni.Kinematics/Linkage.cs
        Main linkage model with joint/link simulation

    Sufni.Kinematics/Joint.cs
        Pivot point definition for linkage

    Sufni.Kinematics/Link.cs
        Connection between joints

    Sufni.Kinematics/KinematicSolver.cs
        Solves linkage positions for given shock stroke

    Sufni.Kinematics/BikeCharacteristics.cs
        Calculated bike properties (anti-squat, leverage ratio, etc.)

    Sufni.Kinematics/GeometryUtils.cs
        Geometric calculation utilities

MAIN APPLICATION
----------------

Sufni.App (Sufni.App/Sufni.App/)
Multi-platform UI application using Avalonia (XAML-based cross-platform UI).
Contains approximately 53 view files with corresponding ViewModels.

    Key directories:
    - Sufni.App/Sufni.App/Views/          XAML UI definitions
    - Sufni.App/Sufni.App/ViewModels/     MVVM view models
    - Sufni.App/Sufni.App/Services/       Business logic and data access
    - Sufni.App/Sufni.App/Models/         Domain entities
    - Sufni.App/Sufni.App/Plots/          Plot rendering logic

    Sufni.App/Sufni.App/App.axaml.cs
        Application entry point and DI container setup

PLATFORM-SPECIFIC PROJECTS
--------------------------

Each platform has its own project that references Sufni.App:

    Sufni.App/Sufni.App.Windows/Program.cs
    Sufni.App/Sufni.App.macOS/Program.cs
    Sufni.App/Sufni.App.Linux/Program.cs
    Sufni.App/Sufni.App.Android/MainActivity.cs
    Sufni.App/Sufni.App.iOS/AppDelegate.cs

SUPPORT LIBRARIES
-----------------

ServiceDiscovery/
mDNS service discovery for detecting network-connected DAQ devices.

    ServiceDiscovery/ServiceDiscovery.common.cs
        Cross-platform interface definition

    ServiceDiscovery/ServiceDiscovery.socket.cs
        Socket-based mDNS implementation (Windows/Linux/Android)

    ServiceDiscovery/ServiceDiscovery.bonjour.cs
        Native Bonjour implementation (macOS/iOS)

SecureStorage/
Platform-specific secure credential storage for JWT refresh tokens.

    SecureStorage/SecureStorage.common.cs
        Cross-platform interface

    SecureStorage/SecureStorage.win.cs
        Windows Data Protection API

    SecureStorage/SecureStorage.linux.cs
        Linux keyring/file storage

    SecureStorage/SecureStorage.android.cs
        Android KeyStore

    SecureStorage/SecureStorage.ios.cs
        iOS Keychain

HapticFeedback/
Platform-specific haptic feedback for mobile devices.

    HapticFeedback/HapticFeedback.common.cs
    HapticFeedback/HapticFeedback.android.cs
    HapticFeedback/HapticFeedback.ios.cs

FriendlyNameProvider/
Device naming utilities for sync identification.

    FriendlyNameProvider/FriendlyNameProvider.common.cs
    FriendlyNameProvider/FriendlyNameProvider.android.cs

2. TELEMETRY FILE SYNC AND IMPORT

The application supports multiple methods for acquiring telemetry data from
the Pico DAQ hardware device.

MASS STORAGE (USB/REMOVABLE MEDIA)
----------------------------------

    Sufni.App/Sufni.App/Services/TelemetryDataStoreService.cs
        Polls mounted drives every 1 second, maintains data store collection

    Sufni.App/Sufni.App/Models/MassStorageTelemetryDataStore.cs
        Identifies DAQ drives by BOARDID marker file, scans for *.SST files

    Sufni.App/Sufni.App/Models/MassStorageTelemetryFile.cs
        Represents a single SST file on mass storage

How it works:
1. Service polls mounted drives every 1 second
2. Identifies DAQ drives by presence of BOARDID marker file
3. BOARDID contains the device GUID for board identification
4. Scans root directory for *.SST files (raw telemetry)
5. On import: files moved from root to uploaded/ subdirectory
6. On trash: files moved to trash/ subdirectory

File structure on DAQ storage:
/BOARDID                    <- Device identifier file
/SESSION_001.SST           <- Raw telemetry files
/SESSION_002.SST
/uploaded/                  <- Already imported files
/trash/                     <- Deleted files

NETWORK/WIFI SYNC (DIRECT TCP)
------------------------------

    Sufni.App/Sufni.App/Models/NetworkTelemetryDataStore.cs
        Connects to DAQ via TCP, exchanges file listings

    Sufni.App/Sufni.App/Models/NetworkTelemetryFile.cs
        Represents a single SST file on network-connected DAQ

    Sufni.App/Sufni.App/Models/SstTcpClient.cs
        TCP client for binary protocol communication with DAQ

How it works:
1. mDNS service discovery listens for "_gosst._tcp" services
2. When DAQ device announces itself, app connects via TCP
3. Binary protocol exchanges file listing:
    - Device metadata (board ID)
    - File names, sizes, timestamps
4. Files transferred directly over TCP socket
5. Same post-import behavior as mass storage

CROSS-DEVICE SYNCHRONIZATION (DESKTOP<->MOBILE)
-----------------------------------------------

This enables a desktop computer to act as a hub/server that mobile devices
can sync with. Useful when DAQ only connects to desktop but user wants
data on phone for trackside analysis.

Server (Desktop):

    Sufni.App/Sufni.App/Services/SynchronizationServerService.cs
        ASP.NET Core embedded HTTP server with JWT auth

    Technology: ASP.NET Core embedded HTTP server
    Security: TLS 1.3, JWT authentication with refresh tokens
    Discovery: mDNS advertisement

    REST API Endpoints:
    - POST /pair/request      <- Mobile initiates pairing
    - POST /pair/confirm      <- Desktop confirms pairing code
    - POST /sync/push         <- Mobile pushes local changes
    - POST /sync/pull         <- Mobile pulls remote changes
    - GET  /session/data/{id} <- Download session telemetry blob
    - PATCH /session/data/{id} <- Upload session telemetry blob

Client (Mobile):

    Sufni.App/Sufni.App/Services/SynchronizationClientService.cs
        Sync client that pushes/pulls changes from server

    Sufni.App/Sufni.App/Services/ISynchronizationClientService.cs
        Interface definition

    Sufni.App/Sufni.App/Services/HttpApiService.cs
        HTTP client wrapper for API calls

    Sufni.App/Sufni.App/Services/IHttpApiService.cs
        Interface definition

    Sync workflow:
    1. Push local changes to server
    2. Pull remote changes from server
    3. Push incomplete sessions (metadata without processed data)
    4. Pull incomplete sessions (download missing data blobs)
    5. Update last sync timestamp

    Entities synchronized:
    - Boards (DAQ device registrations)
    - Bikes (bicycle configurations)
    - Setups (sensor configurations linked to bikes)
    - Sessions (ride recordings with telemetry data)
    - Tracks (GPS traces)

3. RAW DATA FORMAT (SST FILES)

SST files are the raw binary telemetry format written by the Pico DAQ device.

    Sufni.Telemetry/RawTelemetryData.cs
        Parses SST files and applies spike elimination

FILE STRUCTURE
--------------

Offset  Size    Field
------  ----    -----
0       3       Magic number: ASCII "SST"
3       1       Version: Currently 3
4       2       Sample rate: uint16, Hz (typically 1000)
6       8       Timestamp: int64, Unix seconds
14      N       Records: Pairs of int16 values

Each record contains:
- Front suspension sensor reading (int16, raw encoder counts)
- Rear suspension sensor reading (int16, raw encoder counts)

Total samples = (file_size - 14) / 4

SPIKE ELIMINATION
-----------------

Raw sensor data may contain spikes from electrical noise or sensor glitches.
The RawTelemetryData class applies spike elimination before returning data.

Algorithm (DetectSpikes method in Sufni.Telemetry/RawTelemetryData.cs):
1. Use sliding window to detect sudden value changes
2. Window size: 50 samples at 1000Hz = 50ms
3. Threshold: Derivative exceeds 3x median absolute deviation
4. When spike detected, interpolate between last good values
5. Handles both single-sample spikes and sustained baseline shifts

Output: Two ushort arrays (front, rear) with values in range 0-4095
(12-bit ADC resolution, clamped to valid range)

4. DATA PROCESSING PIPELINE

Raw sensor data goes through multiple processing stages to produce
analysis-ready telemetry data.

    Sufni.Telemetry/TelemetryData.cs
        Main processing orchestration, FromRecording() method

    Sufni.Telemetry/Strokes.cs
        Stroke detection and categorization

    Sufni.Telemetry/Filters.cs
        Savitzky-Golay filter implementation

    Sufni.Telemetry/Parameters.cs
        Processing thresholds (stroke length, airtime duration, etc.)

PROCESSING STAGES
-----------------

Stage 1: Raw Data Loading
- Read SST file bytes
- Parse header (magic, version, sample rate, timestamp)
- Apply spike elimination to sensor readings
- Output: Two arrays of raw encoder counts

Stage 2: Travel Calculation
- Apply sensor configuration calibration function
- Converts raw counts to millimeters of suspension travel
- Calibration depends on sensor type (see sensor configs below)
- Clamp values to [0, MaxTravel] to handle calibration errors
- Output: double[] Travel in millimeters

    Sensor configuration files:

    Sufni.App/Sufni.App/Models/SensorConfigurations/SensorConfiguration.cs
        Base class and ISensorConfiguration interface

    Sufni.App/Sufni.App/Models/SensorConfigurations/LinearForkSensorConfiguration.cs
        Linear mapping for linear potentiometer on fork

    Sufni.App/Sufni.App/Models/SensorConfigurations/RotationalForkSensorConfiguration.cs
        Angular mapping for rotary encoder on fork

    Sufni.App/Sufni.App/Models/SensorConfigurations/LinearShockSensorConfiguration.cs
        Linear mapping for shock potentiometer

    Sufni.App/Sufni.App/Models/SensorConfigurations/RotationalShockSensorConfiguration.cs
        Angular mapping for shock encoder

Stage 3: Velocity Calculation
- Apply Savitzky-Golay smoothing filter (Sufni.Telemetry/Filters.cs)
- Parameters: 51-point window, 1st derivative, polynomial order 3
- Produces smoothed velocity while filtering noise
- Positive velocity = compression (suspension compressing)
- Negative velocity = rebound (suspension extending)
- Output: double[] Velocity in mm/s

Stage 4: Stroke Detection (Sufni.Telemetry/Strokes.cs)
- Identify compression and rebound strokes
- A stroke is a continuous motion in one direction
- Filter out small direction changes (noise)
- Concatenate adjacent strokes at top-out regions

    Stroke categorization by length:
    - Compressions: length >= 5mm (positive movement)
    - Rebounds: length <= -5mm (negative movement)
    - Idlings: |length| < 5mm (minimal movement)

    Each stroke records:
    - Start/end sample indices
    - Length (mm of travel change)
    - Duration (seconds)
    - Statistics (sum/max travel, sum/max velocity, bottomout count)
    - Digitized histogram data

Stage 5: Airtime Detection (Sufni.Telemetry/TelemetryData.cs)
- Identifies when bike is airborne (both wheels off ground)
- Air candidate criteria:
- Duration >= 0.2 seconds
- Max velocity >= 500 mm/s
- Max travel <= 5mm (near full extension)
- Airtime confirmed when:
- Front and rear air candidates overlap >= 50%
- OR single suspension idling with mean travel <= 4% of max

Stage 6: Histogram Generation
- Travel histogram: 20 bins from 0 to MaxTravel
- Velocity histogram: Bins at 100 mm/s intervals (coarse)
- Fine velocity histogram: Bins at 15 mm/s intervals
- Frequency histogram: FFT of travel signal, 0-10 Hz range

    FFT analysis (using MathNet.Numerics):
    - Identifies suspension oscillation frequencies
    - Useful for detecting wheel resonance
    - Helps tune damper settings

Stage 7: Statistics Calculation
- Max travel reached
- Average travel
- Bottomout count (travel within 2mm of maximum)
- Average/max compression velocity
- Average/max rebound velocity
- Velocity band percentages (time in low/high speed zones)
- Normal distribution fit for velocity data

5. PROCESSED DATA FORMAT (MESSAGEPACK)

Processed telemetry data is serialized using MessagePack, a fast binary
serialization format. This is stored in the database and transmitted
during synchronization.

    Sufni.Telemetry/TelemetryData.cs
        Root object with [MessagePackObject] attributes

DATA STRUCTURE HIERARCHY
------------------------

TelemetryData (root object)
|
+-- Metadata
|   +-- SourceName: string (original SST filename)
|   +-- Version: byte (SST file version)
|   +-- SampleRate: int (Hz, typically 1000)
|   +-- Timestamp: long (Unix seconds)
|   +-- Duration: double (seconds, calculated)
|
+-- Front: Suspension (front suspension data)
|   +-- Present: bool (false if sensor disconnected)
|   +-- Travel[]: double array (mm, per-sample)
|   +-- Velocity[]: double array (mm/s, per-sample)
|   +-- MaxTravel: double (mm, from sensor config)
|   +-- AnomalyRate: double (% samples with spikes)
|   +-- TravelBins[]: double array (histogram edges)
|   +-- VelocityBins[]: double array (histogram edges)
|   +-- FineVelocityBins[]: double array (fine histogram edges)
|   +-- Strokes: StrokesData
|       +-- Compressions[]: Stroke array
|       +-- Rebounds[]: Stroke array
|       +-- (Idlings[]: not serialized, reconstructed)
|
+-- Rear: Suspension (rear suspension data, same structure as Front)
|
+-- Airtimes[]: Airtime array
+-- Start: double (seconds from session start)
+-- End: double (seconds from session start)

STROKE DATA STRUCTURE (Sufni.Telemetry/Strokes.cs)
--------------------------------------------------

Each Stroke contains:
Start: int (sample index)
End: int (sample index)
Length: double (mm, positive=compression, negative=rebound)
Duration: double (seconds)
Stat: StrokeStat
SumTravel: double (cumulative travel in mm)
MaxTravel: double (peak travel in mm)
SumVelocity: double (cumulative velocity)
MaxVelocity: double (peak velocity in mm/s)
Bottomouts: int (count of near-max-travel samples)
Count: int (number of samples)
DigitizedTravel[]: int array (histogram bin indices)
DigitizedVelocity[]: int array (histogram bin indices)
FineDigitizedVelocity[]: int array (fine histogram bin indices)
AirCandidate: bool (not serialized, calculated at load)

SENSOR CONFIGURATION
--------------------

Sensor configurations are stored as JSON with polymorphic deserialization.

    Sufni.App/Sufni.App/Models/SensorConfigurations/SensorConfiguration.cs
        ISensorConfiguration interface and base implementation

Properties:
Type: string (discriminator for polymorphism)
MeasurementToTravel: Func<ushort, double> (calibration function)
MaxTravel: double (physical suspension limit in mm)

Implementations:
Sufni.App/Sufni.App/Models/SensorConfigurations/LinearForkSensorConfiguration.cs
Sufni.App/Sufni.App/Models/SensorConfigurations/RotationalForkSensorConfiguration.cs
Sufni.App/Sufni.App/Models/SensorConfigurations/LinearShockSensorConfiguration.cs
Sufni.App/Sufni.App/Models/SensorConfigurations/RotationalShockSensorConfiguration.cs

Each type defines its own calibration parameters and travel calculation
formula based on the sensor mounting geometry and characteristics.

6. DATABASE STORAGE

   Sufni.App/Sufni.App/Services/SQLiteDatabaseService.cs
   SQLite implementation with async operations

   Sufni.App/Sufni.App/Services/IDatabaseService.cs
   Database service interface

Technology: SQLite with async operations (sqlite-net-pcl)
Location: Platform-specific app data directory
Windows: %LOCALAPPDATA%/Sufni.App/sst.db
macOS: ~/Library/Application Support/Sufni.App/sst.db
Linux: ~/.local/share/Sufni.App/sst.db

Configuration:
Write-Ahead Logging (WAL) enabled for better concurrency
Async operations throughout for UI responsiveness

DOMAIN MODELS (mapped to tables)
--------------------------------

    Sufni.App/Sufni.App/Models/Session.cs
        Ride recording with telemetry data blob

    Sufni.App/Sufni.App/Models/Bike.cs
        Bicycle configuration with geometry and linkage

    Sufni.App/Sufni.App/Models/Setup.cs
        Links bike with sensor configurations

    Sufni.App/Sufni.App/Models/Board.cs
        DAQ hardware device registration

    Sufni.App/Sufni.App/Models/Track.cs
        GPS trace data

    Sufni.App/Sufni.App/Models/SessionCache.cs
        Cached/computed session data

    Sufni.App/Sufni.App/Models/Synchronizable.cs
        Base class for sync-enabled entities

TABLES
------

session
Primary storage for ride recordings.

    Columns:
    - id: TEXT PRIMARY KEY (GUID)
    - name: TEXT
    - description: TEXT
    - timestamp: INTEGER (Unix seconds)
    - setup_id: TEXT (foreign key to setup)
    - data: BLOB (MessagePack-serialized TelemetryData)
    - has_data: INTEGER (boolean, 1 if data blob present)
    - track: TEXT (JSON, simplified GPS points)
    - full_track_id: TEXT (foreign key to track for full data)

    Suspension settings (per-session tuning notes):
    - front_spring_rate: REAL
    - rear_spring_rate: REAL
    - front_hsc: INTEGER (high-speed compression clicks)
    - front_lsc: INTEGER (low-speed compression clicks)
    - front_lsr: INTEGER (low-speed rebound clicks)
    - front_hsr: INTEGER (high-speed rebound clicks)
    - rear_hsc, rear_lsc, rear_lsr, rear_hsr: INTEGER

    Synchronization fields:
    - updated: INTEGER (server timestamp)
    - client_updated: INTEGER (local timestamp)
    - deleted: INTEGER (soft delete timestamp)

bike
Bicycle configuration including geometry and linkage.

    Columns:
    - id: TEXT PRIMARY KEY (GUID)
    - name: TEXT
    - head_angle: REAL (degrees)
    - fork_stroke: REAL (mm)
    - shock_stroke: REAL (mm)
    - linkage: TEXT (JSON, Linkage model for kinematics)
    - image: BLOB (bike photo)
    - pixels_to_millimeters: REAL (image calibration)
    - updated, client_updated, deleted: INTEGER (sync fields)

setup
Links a bike with sensor configurations.

    Columns:
    - id: TEXT PRIMARY KEY (GUID)
    - name: TEXT
    - bike_id: TEXT (foreign key to bike)
    - front_sensor_configuration: TEXT (JSON, ISensorConfiguration)
    - rear_sensor_configuration: TEXT (JSON, ISensorConfiguration)
    - updated, client_updated, deleted: INTEGER (sync fields)

board
DAQ hardware device registration.

    Columns:
    - id: TEXT PRIMARY KEY (GUID from BOARDID file)
    - setup_id: TEXT (foreign key to setup, nullable)
    - updated, client_updated, deleted: INTEGER (sync fields)

track
Full GPS trace data for rides.

    Columns:
    - id: TEXT PRIMARY KEY (GUID)
    - points: TEXT (JSON array of {lat, lon, time, elevation})
    - start_time: INTEGER (Unix seconds)
    - end_time: INTEGER (Unix seconds)
    - updated, client_updated, deleted: INTEGER (sync fields)

session_cache
Optimized access layer for frequently accessed session data.

    Columns:
    - session_id: TEXT PRIMARY KEY (foreign key to session)
    - (additional cached/computed fields)

sync
Synchronization state tracking.

    Columns:
    - server_url: TEXT PRIMARY KEY
    - last_sync_time: INTEGER (Unix seconds)

paired_device
Device pairing credentials for sync.

    Columns:
    - device_id: TEXT PRIMARY KEY
    - refresh_token: TEXT (JWT refresh token)
    - expires: INTEGER (token expiration)

SOFT DELETE PATTERN
-------------------

All synchronizable entities use soft delete (Sufni.App/Sufni.App/Models/Synchronizable.cs):
- When deleted locally, "deleted" timestamp is set
- Record retained for sync conflict resolution
- Cleanup runs on database initialization
- Records with "deleted" > 1 day old are permanently removed

7. DATA DISPLAY AND VISUALIZATION

UI Framework: Avalonia (cross-platform XAML, similar to WPF)
Plotting: ScottPlot library
Maps: Mapsui library

PLOT IMPLEMENTATIONS
--------------------

    Sufni.App/Sufni.App/Plots/SufniPlot.cs
        Base plot class with common styling

    Sufni.App/Sufni.App/Plots/TelemetryPlot.cs
        Base for time-series telemetry plots

    Sufni.App/Sufni.App/Plots/TravelPlot.cs
        Shows suspension travel over time
        - X axis: Time (seconds)
        - Y axis: Travel (millimeters)
        - Front suspension: Blue line, left Y axis
        - Rear suspension: Orange line, right Y axis
        - Airtimes: Semi-transparent red vertical spans

    Sufni.App/Sufni.App/Plots/VelocityPlot.cs
        Shows suspension velocity over time
        - Positive values: Compression
        - Negative values: Rebound

    Sufni.App/Sufni.App/Plots/TravelHistogramPlot.cs
        Distribution of time spent at each travel position
        - 20 bins from 0 to max travel
        - Separate histograms for compression and rebound

    Sufni.App/Sufni.App/Plots/VelocityHistogramPlot.cs
        Stacked histogram by travel zone
        - 10 travel zones from 0% to 100%
        - Reveals damping characteristics at different stroke positions

    Sufni.App/Sufni.App/Plots/TravelFrequencyHistogramPlot.cs
        FFT analysis of suspension travel
        - X axis: Frequency (Hz, 0-10 Hz)
        - Identifies oscillation frequencies

    Sufni.App/Sufni.App/Plots/BalancePlot.cs
        Front vs rear suspension correlation
        - Scatter plot with polynomial trend lines
        - Calculates Mean Signed Deviation (MSD)

    Sufni.App/Sufni.App/Plots/LeverageRatioPlot.cs
        Suspension kinematics visualization
        - Leverage ratio curve from linkage model

DESKTOP PLOT VIEWS
------------------

    Sufni.App/Sufni.App/DesktopViews/Plots/TravelPlotDesktopView.cs
    Sufni.App/Sufni.App/DesktopViews/Plots/VelocityPlotDesktopView.cs
    Sufni.App/Sufni.App/DesktopViews/Plots/TravelHistogramDesktopView.cs
    Sufni.App/Sufni.App/DesktopViews/Plots/VelocityHistogramDesktopView.cs
    Sufni.App/Sufni.App/DesktopViews/Plots/TravelFrequencyHistogramDesktopView.cs
    Sufni.App/Sufni.App/DesktopViews/Plots/BalancePlotDesktopView.cs

APPLICATION VIEWS
-----------------

Main Navigation:

    Sufni.App/Sufni.App/Views/MainView.axaml
        Root container with navigation tabs

    Sufni.App/Sufni.App/Views/MainPagesView.axaml
        Page container for main sections

Session Views:

    Sufni.App/Sufni.App/Views/ItemLists/SessionListView.axaml
        List of recorded sessions with search/filter

    Sufni.App/Sufni.App/Views/Items/SessionView.axaml
        Detail view for a single session

    Sufni.App/Sufni.App/Views/ImportSessionsView.axaml
        Import dialog for selecting SST files

Session Tab Pages:

    Sufni.App/Sufni.App/Views/SessionPages/SpringPageView.axaml
        Travel analysis for spring tuning

    Sufni.App/Sufni.App/Views/SessionPages/DamperPageView.axaml
        Velocity analysis for damper tuning

    Sufni.App/Sufni.App/Views/SessionPages/BalancePageView.axaml
        Front/rear balance analysis

    Sufni.App/Sufni.App/Views/SessionPages/NotesPageView.axaml
        Session metadata and rider notes

    Sufni.App/Sufni.App/Views/Plots/VelocityBandView.axaml
        Velocity band breakdown (HSC/LSC/HSR/LSR percentages)

    Sufni.App/Sufni.App/Views/MapView.axaml
        GPS visualization with Mapsui

Bike/Setup Views:

    Sufni.App/Sufni.App/Views/ItemLists/BikeListView.axaml
    Sufni.App/Sufni.App/Views/Items/BikeView.axaml
        Bicycle configuration management

    Sufni.App/Sufni.App/Views/ItemLists/SetupListView.axaml
    Sufni.App/Sufni.App/Views/Items/SetupView.axaml
        Sensor configuration management

Sensor Configuration Views:

    Sufni.App/Sufni.App/Views/SensorConfigurations/LinearForkSensorConfigurationView.axaml
    Sufni.App/Sufni.App/Views/SensorConfigurations/RotationalForkSensorConfigurationView.axaml
    Sufni.App/Sufni.App/Views/SensorConfigurations/LinearShockSensorConfigurationView.axaml
    Sufni.App/Sufni.App/Views/SensorConfigurations/RotationalShockSensorConfigurationView.axaml

Sync/Pairing Views:

    Sufni.App/Sufni.App/Views/PairingClientView.axaml
        Mobile pairing UI

8. LOGICAL ARCHITECTURE

The application follows a layered MVVM architecture with dependency injection.

LAYERS
------

Presentation Layer (Views + ViewModels)
- XAML views define UI layout
- ViewModels expose data and commands
- Compiled bindings for performance
- No business logic in views

    Base classes:

    Sufni.App/Sufni.App/ViewModels/ViewModelBase.cs
        Basic observable object

    Sufni.App/Sufni.App/ViewModels/SessionPages/PageViewModelBase.cs
        Page with navigation support

    Sufni.App/Sufni.App/ViewModels/TabPageViewModelBase.cs
        Tabbed page container

    Sufni.App/Sufni.App/ViewModels/Items/ItemViewModelBase.cs
        CRUD item with save/delete

    Sufni.App/Sufni.App/ViewModels/ItemLists/ItemListViewModelBase.cs
        Collection management

Business Logic Layer (Services)

    Sufni.App/Sufni.App/Services/ITelemetryDataStoreService.cs
        Aggregates data sources interface

    Sufni.App/Sufni.App/Services/TelemetryDataStoreService.cs
        Implementation

    Sufni.App/Sufni.App/Services/ISynchronizationClientService.cs
    Sufni.App/Sufni.App/Services/SynchronizationClientService.cs
        Mobile sync client

    Sufni.App/Sufni.App/Services/ISynchronizationServerService.cs
    Sufni.App/Sufni.App/Services/SynchronizationServerService.cs
        Desktop sync server

    Sufni.App/Sufni.App/Services/IDialogService.cs
    Sufni.App/Sufni.App/Services/DialogService.cs
        Platform-agnostic dialogs

    Sufni.App/Sufni.App/Services/IFileService.cs
    Sufni.App/Sufni.App/Services/FileService.cs
        File/folder pickers

Data Access Layer

    Sufni.App/Sufni.App/Services/IDatabaseService.cs
        Abstract database operations interface

    Sufni.App/Sufni.App/Services/SQLiteDatabaseService.cs
        SQLite implementation

    Operations:
    - GetAllAsync<T>(): Get all entities of type
    - GetAsync<T>(id): Get entity by ID
    - GetChangedAsync<T>(since): Get entities changed after timestamp
    - PutAsync<T>(entity): Insert or update
    - DeleteAsync<T>(id): Soft delete

Domain/Model Layer

    Telemetry processing:
    - Sufni.Telemetry/TelemetryData.cs
    - Sufni.Telemetry/RawTelemetryData.cs
    - Sufni.Telemetry/Strokes.cs

    Domain entities:
    - Sufni.App/Sufni.App/Models/Session.cs
    - Sufni.App/Sufni.App/Models/Bike.cs
    - Sufni.App/Sufni.App/Models/Setup.cs
    - Sufni.App/Sufni.App/Models/Board.cs
    - Sufni.App/Sufni.App/Models/Track.cs

    Data store abstractions:
    - Sufni.App/Sufni.App/Models/ITelemetryDataStore.cs
    - Sufni.App/Sufni.App/Models/ITelemetryFile.cs

    Kinematics:
    - Sufni.Kinematics/Linkage.cs
    - Sufni.Kinematics/Joint.cs
    - Sufni.Kinematics/Link.cs

DEPENDENCY INJECTION SETUP
--------------------------

    Sufni.App/Sufni.App/Services/RegisteredServices.cs
        DI container registration

Services registered:
IDatabaseService -> SQLiteDatabaseService (singleton)
ITelemetryDataStoreService -> TelemetryDataStoreService (singleton)
ISynchronizationClientService -> SynchronizationClientService
ISynchronizationServerService -> SynchronizationServerService
IDialogService -> DialogService
IFileService -> FileService
IHttpApiService -> HttpApiService

ViewModels registered:
MainViewModel
SessionListViewModel, SessionViewModel
BikeListViewModel, BikeViewModel
SetupListViewModel, SetupViewModel
BoardListViewModel, BoardViewModel
ImportSessionsViewModel
SyncViewModel
(etc.)

9. KEY DESIGN PATTERNS

SYNCHRONIZABLE BASE PATTERN
---------------------------

    Sufni.App/Sufni.App/Models/Synchronizable.cs
        Base class for all sync-enabled entities

All entities that sync between devices inherit from Synchronizable base class.

Base fields:
Id: Guid (primary key)
Updated: long (server timestamp of last change)
ClientUpdated: long (local timestamp of last change)
Deleted: long? (soft delete timestamp, null if active)

This enables:
- Conflict detection (compare timestamps)
- Soft delete for sync (don't lose deletes)
- Change tracking for incremental sync

SENSOR CONFIGURATION STRATEGY PATTERN
-------------------------------------

Different sensor types require different calibration formulas.

    Sufni.App/Sufni.App/Models/SensorConfigurations/SensorConfiguration.cs
        ISensorConfiguration interface

Interface: ISensorConfiguration
Type: string (discriminator)
MeasurementToTravel: Func<ushort, double>
MaxTravel: double

Implementations:
Sufni.App/Sufni.App/Models/SensorConfigurations/LinearForkSensorConfiguration.cs
Sufni.App/Sufni.App/Models/SensorConfigurations/RotationalForkSensorConfiguration.cs
Sufni.App/Sufni.App/Models/SensorConfigurations/LinearShockSensorConfiguration.cs
Sufni.App/Sufni.App/Models/SensorConfigurations/RotationalShockSensorConfiguration.cs

JSON polymorphic deserialization:
- Type field determines concrete class
- ISensorConfiguration.FromJson() factory method
- Handles forward compatibility

DATA STORE ABSTRACTION
----------------------

Multiple data sources unified under common interface.

    Sufni.App/Sufni.App/Models/ITelemetryDataStore.cs
        Interface for data stores

Interface: ITelemetryDataStore
Methods:
- GetFilesAsync(): List of available SST files
- RefreshAsync(): Update file listing

Properties:
- Name: Human-readable source name
- BoardId: Associated DAQ device (if known)

Implementations:
Sufni.App/Sufni.App/Models/MassStorageTelemetryDataStore.cs
USB/removable media

    Sufni.App/Sufni.App/Models/NetworkTelemetryDataStore.cs
        WiFi-connected DAQ

    Sufni.App/Sufni.App/Models/ITelemetryFile.cs
        Interface for individual files

Interface: ITelemetryFile
Methods:
- GetDataAsync(): Read raw SST bytes
- OnImportedAsync(): Called after successful import
- OnTrashedAsync(): Called when file deleted

Implementations:
Sufni.App/Sufni.App/Models/MassStorageTelemetryFile.cs
Sufni.App/Sufni.App/Models/NetworkTelemetryFile.cs
Sufni.App/Sufni.App/Models/StorageProviderTelemetryFile.cs

MVVM PATTERN
------------

Views: XAML with compiled bindings
- Define layout and visual structure
- Bind to ViewModel properties
- Bind commands to user actions

ViewModels: C# classes with CommunityToolkit.Mvvm
- [ObservableProperty] for bindable properties
- [RelayCommand] for bindable commands
- Implement INotifyPropertyChanged automatically
- No direct UI dependencies

Example (from any ViewModel):
[ObservableProperty]
private string _sessionName;

    [RelayCommand]
    private async Task SaveAsync() { ... }

REACTIVE COLLECTIONS
--------------------

Uses DynamicData library for reactive collection management.

SourceCache<T, TKey>: Mutable cache with change notifications
- Add/Update/Remove operations
- Connect() returns IObservable of changes
- Transform/Filter/Sort operators

ReadOnlyObservableCollection<T>: UI-bound read-only view
- Automatically updates when source changes
- Thread-safe updates via dispatcher

10. DATA FLOW EXAMPLES

IMPORT SESSION FROM DAQ
-----------------------

User action: Select files in import dialog, click Import

Flow:

1. Sufni.App/Sufni.App/ViewModels/ImportSessionsViewModel.cs
   ImportSessionsCommand executes

2. For each selected ITelemetryFile:
   a. Call GetDataAsync() to read raw SST bytes

   b. Parse RawTelemetryData from bytes
   (Sufni.Telemetry/RawTelemetryData.cs)
    - Validate magic number "SST"
    - Extract header (version, sample rate, timestamp)
    - Apply spike elimination to sensor data

   c. Load Setup from database (by board ID or user selection)
    - Get sensor configurations for front/rear

   d. Call TelemetryData.FromRecording()
   (Sufni.Telemetry/TelemetryData.cs)
    - Convert raw counts to travel (mm)
    - Calculate velocity (Savitzky-Golay filter)
    - Detect strokes (compression/rebound/idling)
    - Detect airtimes
    - Generate histograms (travel, velocity, frequency)
    - Calculate statistics

   e. Serialize TelemetryData to MessagePack bytes

   f. Create Session model
   (Sufni.App/Sufni.App/Models/Session.cs)
    - Generate new GUID
    - Set name from filename
    - Set timestamp from SST header
    - Set data blob
    - Link to setup

   g. Call IDatabaseService.PutSessionAsync()
   (Sufni.App/Sufni.App/Services/SQLiteDatabaseService.cs)
    - Insert into SQLite session table

   h. Call ITelemetryFile.OnImportedAsync()
    - Mass storage: Move file to uploaded/ directory
    - Network: Mark as transferred on device

3. Refresh session list in UI

SYNCHRONIZE MOBILE WITH DESKTOP
-------------------------------

User action: Open sync page, tap Sync button

Flow (on mobile client):

1. Sufni.App/Sufni.App/Services/SynchronizationClientService.cs
   SyncAllAsync() executes

2. Get last sync time from database
    - Query sync table for server URL

3. PushLocalChangesAsync()
    - Query boards changed since last sync
    - Query bikes changed since last sync
    - Query setups changed since last sync
    - Query sessions changed since last sync (metadata only)
    - Query tracks changed since last sync
    - POST to /sync/push with SynchronizationData payload
    - Server merges changes

4. PullRemoteChangesAsync()
    - POST to /sync/pull with last sync timestamp
    - Server returns SynchronizationData with changes
    - For each entity type:
        - If server version newer: Update local
        - If server deleted: Mark local as deleted
        - If local deleted and server not: Keep deleted
    - Insert/update into local database

5. PushIncompleteSessionsAsync()
    - Query local sessions with has_data=true
    - For each, check if server has data
    - If not, PATCH /session/data/{id} with MessagePack blob

6. PullIncompleteSessionsAsync()
    - Query local sessions with has_data=false
    - For each, GET /session/data/{id}
    - Store received MessagePack blob in database
    - Set has_data=true

7. UpdateLastSyncTimeAsync()
    - Store current timestamp for next sync

11. DEPENDENCIES

UI FRAMEWORK
Avalonia: Cross-platform XAML UI framework
Avalonia.Controls.DataGrid: Data grid component
CommunityToolkit.Mvvm: MVVM helpers and source generators

PLOTTING
ScottPlot: Scientific plotting library
ScottPlot.Avalonia: Avalonia integration

MAPS
Mapsui: Cross-platform map control
Mapsui.Avalonia: Avalonia integration

DATA/SERIALIZATION
sqlite-net-pcl: SQLite async wrapper
MessagePack: Fast binary serialization
System.Text.Json: JSON serialization (built-in)

MATHEMATICS
MathNet.Numerics: FFT, statistics, interpolation, polynomial fitting

NETWORKING
Microsoft.AspNetCore: Embedded HTTP server (sync)
System.Net.Http: HTTP client (built-in)

SECURITY
System.IdentityModel.Tokens.Jwt: JWT token handling
System.Security.Cryptography: TLS, certificates

SERVICE DISCOVERY
Makaretu.Dns: mDNS implementation
Makaretu.Dns.Multicast: Multicast DNS

DEPENDENCY INJECTION
Microsoft.Extensions.DependencyInjection: DI container

REACTIVE EXTENSIONS
DynamicData: Reactive collection management
System.Reactive: Reactive extensions