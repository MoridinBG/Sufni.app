using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Services;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal sealed class LiveCaptureCoordinator : ILiveCaptureCoordinator
{


    private readonly LiveDaqSessionContext context;
    private readonly ILiveDaqSharedStream sharedStream;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ILiveSignalPipeline signalPipeline;
    private readonly LiveAnalysisScheduler analysisScheduler;
    private LiveAnalysisKey? settledAnalysisKey;
    private readonly LiveCaptureRecorder recorder = new();
    private readonly LiveTrackProjector trackProjector = new();
    private readonly LiveDisplayProjector displayProjector;
    private readonly System.Threading.Lock gate = new();
    private readonly LiveCaptureStatePublisher publisher;
    private IDisposable? framesSubscription;
    private IDisposable? statesSubscription;
    private ILiveDaqSharedStreamLease? observerLease;
    private ILiveDaqSharedStreamLease? configurationLockLease;
    private readonly LiveDisplayFeed displayFeed;
    private LiveSessionHeader? sessionHeader;
    private LiveSessionAnalysis? analysis;
    private LiveAnalysisFailure? analysisFailure;
    private LiveAnalysisOptions? analysisOptions;
    private LiveConnectionState connectionState = new LiveConnectionState.Disconnected();
    private string? lastError;
    private long captureRevision;
    private long stateRevision;
    private long resetGeneration;

    private enum CapturePhase { Active, AwaitingReset, Closed, Disposed }
    private CapturePhase phase = CapturePhase.Active;

    public LiveCaptureCoordinator(
        LiveDaqSessionContext context,
        ILiveDaqSharedStream sharedStream,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ILiveSignalPipeline signalPipeline,
        TimeProvider timeProvider,
        IUiThreadDispatcher uiThreadDispatcher)
    {
        this.context = context;
        this.sharedStream = sharedStream;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.signalPipeline = signalPipeline;
        displayFeed = new LiveDisplayFeed(signalPipeline);
        analysisScheduler = new LiveAnalysisScheduler(timeProvider, backgroundTaskRunner,
            sessionPresentationService, CreateAnalysisJob, AcceptAnalysisCompletion, context.IdentityKey);
        displayProjector = new LiveDisplayProjector(context.BikeData);
        publisher = new LiveCaptureStatePublisher(uiThreadDispatcher, timeProvider,
            () => { lock (gate) return stateRevision; },
            () => { lock (gate) return BuildStateLocked(); });
    }

    public IObservable<LiveCaptureState> States => publisher.States;

    public IObservable<LiveSignalBatch> SignalBatches => signalPipeline.SignalBatches;

    public LiveCaptureState Current => publisher.Current;

    public async Task<LivePreviewStartResult?> AttachAsync(CancellationToken cancellationToken = default)
    {
        bool shouldStart;
        LiveConnectionState.Closed? attachClosed = null;
        IDisposable? attachedFramesSubscription = null;
        IDisposable? attachedStatesSubscription = null;
        ILiveDaqSharedStreamLease? attachedObserverLease = null;
        ILiveDaqSharedStreamLease? attachedConfigurationLockLease = null;

        try
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (observerLease is null)
                {
                    var observerResult = sharedStream.AcquireLease();
                    if (observerResult is LiveStreamLeaseResult.Closed observerClosed) attachClosed = observerClosed.State;
                    else
                    {
                        attachedObserverLease = ((LiveStreamLeaseResult.Acquired)observerResult).Lease;
                        var lockResult = sharedStream.AcquireConfigurationLock();
                        if (lockResult is LiveStreamLeaseResult.Closed lockClosed) attachClosed = lockClosed.State;
                        else
                        {
                            attachedConfigurationLockLease = ((LiveStreamLeaseResult.Acquired)lockResult).Lease;
                            displayFeed.Start();
                            attachedFramesSubscription = sharedStream.Frames.Subscribe(HandleFrame);
                            attachedStatesSubscription = sharedStream.States.Subscribe(HandleSharedStreamState);
                            observerLease = attachedObserverLease;
                            configurationLockLease = attachedConfigurationLockLease;
                            framesSubscription = attachedFramesSubscription;
                            statesSubscription = attachedStatesSubscription;
                        }
                    }
                }

                shouldStart = phase == CapturePhase.Active;
            }

            HandleSharedStreamState(sharedStream.CurrentState);
            if (attachClosed is not null)
            {
                if (attachedObserverLease is not null) await attachedObserverLease.DisposeAsync();
                return new LivePreviewStartResult.Closed(attachClosed);
            }

            if (!shouldStart)
            {
                return sharedStream.CurrentState.ConnectionState is LiveConnectionState.Closed closed
                    ? new LivePreviewStartResult.Closed(closed) : null;
            }

            var result = await sharedStream.ConnectLiveAsync(cancellationToken);
            HandleSharedStreamState(sharedStream.CurrentState);
            return result;
        }
        catch
        {
            if (attachedObserverLease is not null || attachedConfigurationLockLease is not null)
            {
                lock (gate)
                {
                    if (ReferenceEquals(framesSubscription, attachedFramesSubscription))
                    {
                        framesSubscription = null;
                    }

                    if (ReferenceEquals(statesSubscription, attachedStatesSubscription))
                    {
                        statesSubscription = null;
                    }

                    if (ReferenceEquals(configurationLockLease, attachedConfigurationLockLease))
                    {
                        configurationLockLease = null;
                    }

                    if (ReferenceEquals(observerLease, attachedObserverLease))
                    {
                        observerLease = null;
                    }
                }

                attachedFramesSubscription?.Dispose();
                attachedStatesSubscription?.Dispose();

                if (attachedConfigurationLockLease is not null)
                {
                    await attachedConfigurationLockLease.DisposeAsync();
                }

                if (attachedObserverLease is not null)
                {
                    await attachedObserverLease.DisposeAsync();
                }
            }

            throw;
        }
    }

    public void UpdateAnalysisOptions(LiveAnalysisOptions options)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (analysisOptions == options) return;
            analysisOptions = options;
        }
        analysisScheduler.Wake();
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            recorder.Reset();
            displayProjector.Reset();
            analysis = null;
            analysisFailure = null;
            trackProjector.Reset();
            captureRevision++;
            resetGeneration++;
            signalPipeline.BeginEpoch(resetGeneration);
            settledAnalysisKey = null;

            if (phase == CapturePhase.AwaitingReset)
            {
                var state = sharedStream.CurrentState;
                connectionState = state.ConnectionState;
                analysisScheduler.ObserveDrops(state.ClientDropCounters);
                if (!state.IsClosed && state.SessionHeader is { } currentHeader)
                {
                    sessionHeader = currentHeader;
                    recorder.ResetImuLocations(sessionHeader);
                    phase = CapturePhase.Active;
                    lastError = state.LastError;
                }
                else if (state.IsClosed)
                {
                    phase = CapturePhase.Closed;
                    lastError = state.LastError;
                }
            }

            stateRevision++;
        }

        analysisScheduler.Reset();
        publisher.Request();
        return Task.CompletedTask;
    }

    public async Task<LiveSessionCapturePackage> PrepareForSaveAsync(CancellationToken cancellationToken = default)
    {
        LiveCaptureContent captureSnapshot;
        long revision;

        lock (gate)
        {
            ThrowIfDisposed();
            if (!recorder.CanSave)
            {
                throw new InvalidOperationException("No live capture is available to save.");
            }

            captureSnapshot = CreateCaptureContentLocked();
            revision = captureRevision;
        }

        var capture = await backgroundTaskRunner.RunAsync(
            () => LiveCaptureMaterializer.Materialize(captureSnapshot, context.BikeData),
            cancellationToken);
        return new LiveSessionCapturePackage(context, capture) { CaptureRevision = revision };
    }

    public async ValueTask DisposeAsync()
    {
        IDisposable? frames;
        IDisposable? states;
        ILiveDaqSharedStreamLease? configurationLock;
        ILiveDaqSharedStreamLease? observer;

        lock (gate)
        {
            if (phase == CapturePhase.Disposed)
            {
                return;
            }

            phase = CapturePhase.Disposed;
            frames = framesSubscription;
            states = statesSubscription;
            configurationLock = configurationLockLease;
            observer = observerLease;
            framesSubscription = null;
            statesSubscription = null;
            configurationLockLease = null;
            observerLease = null;
        }

        frames?.Dispose();
        states?.Dispose();

        var displayDrain = displayFeed.DrainAsync();
        await analysisScheduler.DisposeAsync();
        await displayDrain;

        if (configurationLock is not null)
        {
            await configurationLock.DisposeAsync();
        }

        if (observer is not null)
        {
            await observer.DisposeAsync();
        }

        await displayFeed.DisposeAsync();

        await publisher.CompleteAsync();
    }

    private void HandleSharedStreamState(LiveDaqSharedStreamState state)
    {
        bool changed;
        lock (gate)
        {
            if (phase == CapturePhase.Disposed) return;
            var before = BuildControlsLocked();
            connectionState = state.ConnectionState;
            lastError = state.LastError;
            if (state.SessionHeader is { } nextHeader)
            {
                if (sessionHeader is not null && nextHeader.SessionId != sessionHeader.SessionId && recorder.HasAnyCapture)
                {
                    phase = state.IsClosed ? CapturePhase.Closed : CapturePhase.AwaitingReset;
                }
                else
                {
                    if (sessionHeader is null || nextHeader.SessionId != sessionHeader.SessionId)
                    {
                        displayProjector.Reset();
                        recorder.ResetImuLocations(nextHeader);
                    }
                    sessionHeader = nextHeader;
                    recorder.EnsureImuLocations(sessionHeader);
                }
            }
            if (state.IsClosed) phase = CapturePhase.Closed;
            changed = before != BuildControlsLocked();
            if (changed) stateRevision++;
        }
        analysisScheduler.ObserveDrops(state.ClientDropCounters);
        if (changed) publisher.Request();
    }

    private LiveSignalPresence SignalPresence => new(recorder.HasTravelData, recorder.HasImuData, displayProjector.HasPitchRollData);

    private void HandleFrame(LiveProtocolFrame frame)
    {
        LiveDisplayUpdate? displayUpdate = null;
        bool urgent;
        var changed = false;
        lock (gate)
        {
            if (phase != CapturePhase.Active || !IsCurrentSessionFrameLocked(frame)) return;
            var wasSaveable = recorder.CanSave;
            var previousPresence = SignalPresence;
            switch (frame)
            {
                case LiveTravelBatchFrame travelFrame:
                    if (recorder.AppendTravel(travelFrame, sessionHeader) is { } travel)
                    {
                        changed = true;
                        displayUpdate = displayProjector.ProjectTravel(travel, resetGeneration);
                    }
                    break;
                case LiveImuBatchFrame imuFrame:
                    if (recorder.AppendImu(imuFrame, sessionHeader) is { } imu)
                    {
                        changed = true;
                        displayUpdate = displayProjector.ProjectImu(imu, sessionHeader!, resetGeneration);
                    }
                    break;
                case LiveTemperatureBatchFrame temperature:
                    changed = recorder.AppendTemperature(temperature, sessionHeader);
                    break;
                case LiveGpsBatchFrame gps:
                    changed = recorder.AppendGps(gps, sessionHeader);
                    if (changed) trackProjector.Append(gps.Records, recorder.CreateGpsSnapshot);
                    break;
                case LiveMarkerBatchFrame marker:
                    changed = recorder.AppendMarkers(marker);
                    break;
                case LiveSessionResultFrame result:
                    recorder.RecordFinalStatus(result.Payload.FinalStatus);
                    changed = true;
                    break;
            }
            if (changed)
            {
                captureRevision++;
                stateRevision++;
            }
            urgent = recorder.CanSave != wasSaveable || SignalPresence != previousPresence;
        }
        if (urgent) publisher.Request();
        if (displayUpdate is not null && displayFeed.Offer(displayUpdate)) analysisScheduler.ReportPressure();
        if (changed) analysisScheduler.Wake();
    }

    private bool IsCurrentSessionFrameLocked(LiveProtocolFrame frame)
    {
        uint? frameSessionId = frame switch
        {
            LiveTravelBatchFrame travel => travel.Batch.SessionId,
            LiveImuBatchFrame imu => imu.Batch.SessionId,
            LiveTemperatureBatchFrame temperature => temperature.Batch.SessionId,
            LiveGpsBatchFrame gps => gps.Batch.SessionId,
            LiveBatteryBatchFrame battery => battery.Batch.SessionId,
            LiveMarkerBatchFrame marker => marker.Batch.SessionId,
            LiveSessionStatsFrame stats => stats.Payload.SessionId,
            LiveSessionResultFrame result => result.Payload.SessionId,
            _ => null,
        };

        return frameSessionId is null ||
            (sessionHeader is not null && frameSessionId.Value == sessionHeader.SessionId);
    }

    private LiveAnalysisJob? CreateAnalysisJob()
    {
        lock (gate)
        {
            if (phase == CapturePhase.Disposed || analysisOptions is null || !CanBuildAnalysisLocked()) return null;
            var key = new LiveAnalysisKey(resetGeneration, captureRevision, analysisOptions);
            if (key == settledAnalysisKey || key == analysis?.Key) return null;
            return new LiveAnalysisJob(key, CreateCaptureContentLocked(), context.BikeData);
        }
    }

    private void AcceptAnalysisCompletion(LiveAnalysisCompletion result)
    {
        lock (gate)
        {
            if (phase == CapturePhase.Disposed || result.Key.ResetGeneration != resetGeneration ||
                result.Key.Options != analysisOptions ||
                (settledAnalysisKey is { } settled && result.Key.CaptureRevision < settled.CaptureRevision)) return;
            settledAnalysisKey = result.Key;
            switch (result)
            {
                case LiveAnalysisCompletion.Computed computed:
                    analysis = computed.Analysis;
                    analysisFailure = null;
                    break;
                case LiveAnalysisCompletion.Failed failed:
                    analysisFailure = failed.Failure;
                    break;
            }
            stateRevision++;
        }
        publisher.Request();
    }

    private LiveCaptureState BuildStateLocked() => new(
        stateRevision, captureRevision, BuildControlsLocked(), trackProjector.Points,
        SignalPresence, analysis, analysisFailure);

    private LiveCaptureControls BuildControlsLocked() => new(
        connectionState, lastError, sessionHeader, recorder.CaptureStartUtc,
        recorder.CalculateDuration(sessionHeader), recorder.CanSave,
        phase switch
        {
            CapturePhase.Active => LiveCapturePhase.Active,
            CapturePhase.AwaitingReset => LiveCapturePhase.AwaitingReset,
            _ => LiveCapturePhase.Closed,
        });

    private LiveCaptureContent CreateCaptureContentLocked() =>
        recorder.CreateContent(context.DisplayName, sessionHeader,
            phase is CapturePhase.AwaitingReset or CapturePhase.Closed);

    private bool CanBuildAnalysisLocked()
    {
        return recorder.CanSave && sessionHeader is not null && sessionHeader.AcceptedTravelHz > 0;
    }

    private void ThrowIfDisposed()
    {
        if (phase == CapturePhase.Disposed)
        {
            throw new ObjectDisposedException(nameof(LiveCaptureCoordinator));
        }
    }
}
